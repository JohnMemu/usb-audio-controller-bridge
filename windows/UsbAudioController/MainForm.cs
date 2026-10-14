using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace UsbAudioController;

internal sealed class MainForm : Form
{
    private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupRegistryName = "UsbAudioControllerBridge";

    private readonly Label _addressLabel = new();
    private readonly Label _controllerStatus = new();
    private readonly Label _audioStatus = new();
    private readonly Button _controllerButton = new();
    private readonly Button _audioButton = new();
    private readonly CheckBox _startupCheckBox = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 1000 };
    private readonly NotifyIcon _trayIcon;
    private readonly Icon _applicationIcon;
    private readonly bool _launchInTray;
    private ControllerReceiver? _controller;
    private AudioStreamServer? _audio;
    private PcDiscoveryServer? _discovery;
    private bool _controllerStarted;
    private bool _audioStarted;
    private bool _exitRequested;
    private string? _discoveryError;

    public MainForm(bool launchInTray)
    {
        _launchInTray = launchInTray;
        _applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? throw new InvalidOperationException("The application icon could not be loaded.");
        Icon = _applicationIcon;
        Text = "USB Audio & Controller Bridge";
        MinimumSize = new Size(560, 420);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 8,
            AutoSize = false
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        var title = new Label
        {
            Text = "Android ↔ Windows bridge",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        _addressLabel.AutoSize = true;
        _addressLabel.Margin = new Padding(0, 0, 0, 8);
        _startupCheckBox.Text = "Open with Windows and start in the system tray";
        _startupCheckBox.AutoSize = true;
        _startupCheckBox.Checked = IsStartupEnabled();
        _startupCheckBox.CheckedChanged += StartupCheckBoxChanged;
        _controllerStatus.AutoSize = true;
        _controllerStatus.Text = "Controller: stopped.";
        _controllerStatus.Margin = new Padding(0, 8, 0, 6);
        _audioStatus.AutoSize = true;
        _audioStatus.Text = "Audio: stopped.";
        _audioStatus.Margin = new Padding(0, 8, 0, 6);
        _controllerButton.Text = "Start controller receiver";
        _controllerButton.AutoSize = true;
        _controllerButton.Anchor = AnchorStyles.Left;
        _controllerButton.Click += (_, _) => ToggleController();
        _audioButton.Text = "Start PC audio stream";
        _audioButton.AutoSize = true;
        _audioButton.Anchor = AnchorStyles.Left;
        _audioButton.Click += (_, _) => ToggleAudio();

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(_addressLabel, 0, 1);
        layout.Controls.Add(_startupCheckBox, 0, 2);
        layout.Controls.Add(_controllerButton, 0, 4);
        layout.Controls.Add(_controllerStatus, 0, 5);
        layout.Controls.Add(_audioButton, 0, 6);
        layout.Controls.Add(_audioStatus, 0, 7);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowWindow());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        _trayIcon = new NotifyIcon
        {
            Text = "USB Audio & Controller Bridge",
            Icon = _applicationIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowWindow();

        if (_launchInTray)
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
        }
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                ShowInTaskbar = false;
            }
        };

        _refreshTimer.Tick += (_, _) => RefreshAddress();
        _refreshTimer.Start();
        RefreshAddress();
        Shown += (_, _) =>
        {
            StartDiscovery();
            ToggleController();
            ToggleAudio();
            if (_launchInTray)
                BeginInvoke((Action)Hide);
        };
        FormClosing += (_, args) =>
        {
            if (!_exitRequested)
            {
                args.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                return;
            }

            StopServices();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _applicationIcon.Dispose();
        };
    }

    private void ToggleController()
    {
        try
        {
            if (_controllerStarted)
            {
                _controller?.Stop();
                _controllerStarted = false;
                _controllerButton.Text = "Start controller receiver";
                _controllerStatus.Text = "Controller: stopped.";
                return;
            }

            _controller = new ControllerReceiver(SetControllerStatus);
            _controller.Start();
            _controllerStarted = true;
            _controllerButton.Text = "Stop controller receiver";
            _controllerStatus.Text = "Controller: listening; waiting for Android input.";
        }
        catch (Exception ex)
        {
            _controller?.Dispose();
            _controller = null;
            _controllerStarted = false;
            _controllerButton.Text = "Start controller receiver";
            _controllerStatus.Text = $"Controller start failed: {ex.Message}";
        }
    }

    private void ToggleAudio()
    {
        try
        {
            if (_audioStarted)
            {
                _audio?.Stop();
                _audioStarted = false;
                _audioButton.Text = "Start PC audio stream";
                _audioStatus.Text = "Audio: stopped.";
                return;
            }

            _audio = new AudioStreamServer(SetAudioStatus);
            _audio.Start();
            _audioStarted = true;
            _audioButton.Text = "Stop PC audio stream";
            _audioStatus.Text = _audio.Status;
        }
        catch (Exception ex)
        {
            _audio?.Dispose();
            _audio = null;
            _audioStarted = false;
            _audioButton.Text = "Start PC audio stream";
            _audioStatus.Text = $"Audio start failed: {ex.Message}";
        }
    }

    private void StartDiscovery()
    {
        try
        {
            _discovery = new PcDiscoveryServer();
            _discovery.Start();
        }
        catch (Exception ex)
        {
            _discoveryError = $"Automatic discovery could not start: {ex.Message}";
            RefreshAddress();
        }
    }

    private void RefreshAddress()
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork &&
                              !IPAddress.IsLoopback(address.Address) &&
                              !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Select(address => address.Address.ToString())
            .Distinct()
            .OrderBy(address => address, StringComparer.Ordinal)
            .ToArray();
        var addressText = addresses.Length == 0
            ? "No active IPv4 address found. Connect USB tethering or Wi-Fi."
            : $"Android auto-discovery: UDP 17052\nManual IPv4 options: {string.Join("   •   ", addresses)}";
        _addressLabel.Text = _discoveryError is null ? addressText : $"{addressText}\n{_discoveryError}";
    }

    private bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, false);
        return key?.GetValue(StartupRegistryName) is string;
    }

    private void SetStartupEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath, true)
                ?? throw new InvalidOperationException("Could not open the current-user startup registry key.");
            if (enabled)
                key.SetValue(StartupRegistryName, $"\"{Application.ExecutablePath}\" --startup");
            else
                key.DeleteValue(StartupRegistryName, false);
        }
        catch (Exception ex)
        {
            _startupCheckBox.CheckedChanged -= StartupCheckBoxChanged;
            _startupCheckBox.Checked = !enabled;
            _startupCheckBox.CheckedChanged += StartupCheckBoxChanged;
            MessageBox.Show(this, $"Could not update the Windows startup setting: {ex.Message}",
                "Startup setting failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StartupCheckBoxChanged(object? sender, EventArgs args) =>
        SetStartupEnabled(_startupCheckBox.Checked);

    private void ShowWindow()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }

    private void SetControllerStatus(string text)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(() => _controllerStatus.Text = text);
        else _controllerStatus.Text = text;
    }

    private void SetAudioStatus(string text)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(() => _audioStatus.Text = text);
        else _audioStatus.Text = text;
    }

    private void StopServices()
    {
        _refreshTimer.Stop();
        _discovery?.Dispose();
        _controller?.Dispose();
        _audio?.Dispose();
    }
}
