using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace UsbAudioController;

internal sealed class MainForm : Form
{
    private readonly Label _addressLabel = new();
    private readonly Label _controllerStatus = new();
    private readonly Label _audioStatus = new();
    private readonly Button _controllerButton = new();
    private readonly Button _audioButton = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 500 };
    private ControllerReceiver? _controller;
    private AudioStreamServer? _audio;
    private bool _controllerStarted;
    private bool _audioStarted;

    public MainForm()
    {
        Text = "USB Audio & Controller Bridge";
        MinimumSize = new Size(560, 390);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 7,
            AutoSize = false
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        Controls.Add(layout);

        var title = new Label
        {
            Text = "Android ↔ Windows bridge",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        _addressLabel.AutoSize = true;
        _addressLabel.Margin = new Padding(0, 0, 0, 16);
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
        layout.Controls.Add(_controllerButton, 0, 3);
        layout.Controls.Add(_controllerStatus, 0, 4);
        layout.Controls.Add(_audioButton, 0, 5);
        layout.Controls.Add(_audioStatus, 0, 6);

        _refreshTimer.Tick += (_, _) => RefreshStatus();
        _refreshTimer.Start();
        RefreshStatus();
        FormClosing += (_, _) => StopServices();
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
            _audioStatus.Text = $"Audio start failed: {ex.Message}";
        }
    }

    private void RefreshStatus()
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
        _addressLabel.Text = addresses.Length == 0
            ? "No active IPv4 address found. Connect USB tethering or Wi-Fi."
            : $"Enter this PC address in Android:\n{string.Join("   •   ", addresses)}\nController UDP 17050  |  Audio TCP 17051";

        if (_controllerStarted && _controller is not null)
            _controllerStatus.Text = $"Controller: listening; {_controller.PacketsReceived:N0} packets received.";
        if (_audioStarted && _audio is not null)
            _audioStatus.Text = _audio.Status;
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
        _controller?.Dispose();
        _audio?.Dispose();
    }
}
