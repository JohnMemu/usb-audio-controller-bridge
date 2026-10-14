using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace UsbAudioController;

internal sealed class ControllerReceiver : IDisposable
{
    private const int Port = 17050;
    private const int PacketSize = 40;
    private const uint Magic = 0x43425553;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMilliseconds(500);

    private readonly object _reportLock = new();
    private readonly Action<string> _statusChanged;
    private ViGEmClient? _vigem;
    private IXbox360Controller? _controller;
    private UdpClient? _udp;
    private CancellationTokenSource? _cancellation;
    private Task? _receiveTask;
    private System.Threading.Timer? _staleTimer;
    private uint _lastSequence;
    private bool _hasSequence;
    private long _lastPacketAt;
    private bool _neutralized = true;
    private long _packetsReceived;

    public ControllerReceiver(Action<string> statusChanged) => _statusChanged = statusChanged;

    public long PacketsReceived => Interlocked.Read(ref _packetsReceived);

    public void Start()
    {
        if (_udp is not null) return;

        var udp = new UdpClient(new IPEndPoint(IPAddress.Any, Port));
        ViGEmClient? vigem = null;
        IXbox360Controller? controller = null;
        try
        {
            vigem = new ViGEmClient();
            controller = vigem.CreateXbox360Controller();
            controller.AutoSubmitReport = false;
            controller.Connect();
            _udp = udp;
            _vigem = vigem;
            _controller = controller;
            _cancellation = new CancellationTokenSource();
            Interlocked.Exchange(ref _lastPacketAt, Stopwatch.GetTimestamp());
            _neutralized = true;
            _staleTimer = new System.Threading.Timer(CheckForStaleInput, null, 100, 100);
            _receiveTask = ReceiveLoopAsync(_cancellation.Token);
            _statusChanged($"Controller receiver listening on UDP {Port}; virtual Xbox controller connected.");
        }
        catch
        {
            udp.Dispose();
            controller?.Disconnect();
            vigem?.Dispose();
            throw;
        }
    }

    public void Stop()
    {
        var cancellation = _cancellation;
        if (cancellation is null) return;

        _cancellation = null;
        _staleTimer?.Dispose();
        _staleTimer = null;
        cancellation.Cancel();
        _udp?.Dispose();
        _udp = null;
        try
        {
            _receiveTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        _receiveTask = null;

        lock (_reportLock)
        {
            if (_controller is not null)
            {
                SendNeutralReport(_controller);
                _controller.Disconnect();
            }
            _controller = null;
        }

        _vigem?.Dispose();
        _vigem = null;
        cancellation.Dispose();
        _statusChanged("Controller receiver stopped.");
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await _udp!.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (!TryParse(result.Buffer, out var packet)) continue;
                if (_hasSequence && !IsNewerSequence(packet.Sequence, _lastSequence)) continue;

                _lastSequence = packet.Sequence;
                _hasSequence = true;
                lock (_reportLock)
                {
                    if (_controller is null) break;
                    ApplyReport(_controller, packet);
                    Interlocked.Exchange(ref _lastPacketAt, Stopwatch.GetTimestamp());
                    _neutralized = false;
                }

                Interlocked.Increment(ref _packetsReceived);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (SocketException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _statusChanged($"Controller receiver failed: {ex.Message}");
        }
    }

    private void CheckForStaleInput(object? state)
    {
        if (_controller is null || _neutralized) return;
        if (Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastPacketAt)) <= StaleAfter) return;

        lock (_reportLock)
        {
            if (_controller is null || _neutralized) return;
            SendNeutralReport(_controller);
            _neutralized = true;
        }
    }

    private static bool TryParse(byte[] data, out ControllerPacket packet)
    {
        packet = default;
        if (data.Length != PacketSize || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
            return false;

        var values = new float[6];
        for (var i = 0; i < values.Length; i++)
            values[i] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8 + i * 4, 4)));

        if (values.Any(value => !float.IsFinite(value))) return false;
        packet = new ControllerPacket(
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4)),
            values[0], values[1], values[2], values[3], values[4], values[5],
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(32, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(34, 2)));
        return true;
    }

    private static void ApplyReport(IXbox360Controller controller, ControllerPacket packet)
    {
        controller.LeftThumbX = ToAxis(packet.Lx);
        controller.LeftThumbY = ToAxis(packet.Ly);
        controller.RightThumbX = ToAxis(packet.Rx);
        controller.RightThumbY = ToAxis(packet.Ry);
        controller.LeftTrigger = ToTrigger(packet.Lt);
        controller.RightTrigger = ToTrigger(packet.Rt);

        controller.SetButtonState(Xbox360Button.A, IsSet(packet.Buttons, 0));
        controller.SetButtonState(Xbox360Button.B, IsSet(packet.Buttons, 1));
        controller.SetButtonState(Xbox360Button.X, IsSet(packet.Buttons, 2));
        controller.SetButtonState(Xbox360Button.Y, IsSet(packet.Buttons, 3));
        controller.SetButtonState(Xbox360Button.LeftShoulder, IsSet(packet.Buttons, 4));
        controller.SetButtonState(Xbox360Button.RightShoulder, IsSet(packet.Buttons, 5));
        controller.SetButtonState(Xbox360Button.Back, IsSet(packet.Buttons, 6));
        controller.SetButtonState(Xbox360Button.Start, IsSet(packet.Buttons, 7));
        controller.SetButtonState(Xbox360Button.LeftThumb, IsSet(packet.Buttons, 8));
        controller.SetButtonState(Xbox360Button.RightThumb, IsSet(packet.Buttons, 9));
        controller.SetButtonState(Xbox360Button.Guide, IsSet(packet.Buttons, 10));
        controller.SetButtonState(Xbox360Button.Up, IsSet(packet.Dpad, 0));
        controller.SetButtonState(Xbox360Button.Down, IsSet(packet.Dpad, 1));
        controller.SetButtonState(Xbox360Button.Left, IsSet(packet.Dpad, 2));
        controller.SetButtonState(Xbox360Button.Right, IsSet(packet.Dpad, 3));
        controller.SubmitReport();
    }

    private static void SendNeutralReport(IXbox360Controller controller)
    {
        controller.LeftThumbX = 0;
        controller.LeftThumbY = 0;
        controller.RightThumbX = 0;
        controller.RightThumbY = 0;
        controller.LeftTrigger = 0;
        controller.RightTrigger = 0;
        controller.SetButtonState(Xbox360Button.A, false);
        controller.SetButtonState(Xbox360Button.B, false);
        controller.SetButtonState(Xbox360Button.X, false);
        controller.SetButtonState(Xbox360Button.Y, false);
        controller.SetButtonState(Xbox360Button.LeftShoulder, false);
        controller.SetButtonState(Xbox360Button.RightShoulder, false);
        controller.SetButtonState(Xbox360Button.Back, false);
        controller.SetButtonState(Xbox360Button.Start, false);
        controller.SetButtonState(Xbox360Button.LeftThumb, false);
        controller.SetButtonState(Xbox360Button.RightThumb, false);
        controller.SetButtonState(Xbox360Button.Guide, false);
        controller.SetButtonState(Xbox360Button.Up, false);
        controller.SetButtonState(Xbox360Button.Down, false);
        controller.SetButtonState(Xbox360Button.Left, false);
        controller.SetButtonState(Xbox360Button.Right, false);
        controller.SubmitReport();
    }

    private static bool IsSet(ushort mask, int bit) => (mask & (1 << bit)) != 0;
    private static bool IsNewerSequence(uint candidate, uint previous) => unchecked((int)(candidate - previous)) > 0;
    private static short ToAxis(float value) => (short)Math.Round(Math.Clamp(value, -1f, 1f) * 32767f);
    private static byte ToTrigger(float value) => (byte)Math.Round(Math.Clamp(value, 0f, 1f) * 255f);

    public void Dispose() => Stop();

    private readonly record struct ControllerPacket(uint Sequence, float Lx, float Ly, float Rx, float Ry, float Lt, float Rt, ushort Buttons, ushort Dpad);
}
