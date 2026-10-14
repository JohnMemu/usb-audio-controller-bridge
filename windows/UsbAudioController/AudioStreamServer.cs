using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace UsbAudioController;

internal sealed class AudioStreamServer : IDisposable
{
    private const int Port = 17051;
    private const int HeaderSize = 16;
    private const int CaptureBufferMilliseconds = 10;
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly Action<string> _statusChanged;
    private readonly ConcurrentDictionary<Guid, AudioClient> _clients = new();
    private WasapiCapture? _capture;
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private Task? _acceptTask;
    private int _sampleRate;
    private int _channels;
    private int _sourceBits;
    private bool _sourceIsFloat;
    private volatile string _status = "Audio server stopped.";

    public AudioStreamServer(Action<string> statusChanged) => _statusChanged = statusChanged;
    public string Status => _status;
    public int ClientCount => _clients.Count;

    public void Start()
    {
        if (_capture is not null) return;

        var capture = new LowLatencyLoopbackCapture();
        try
        {
            ConfigureSourceFormat(capture.WaveFormat);
            var listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();
            var cancellation = new CancellationTokenSource();
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            _listener = listener;
            _capture = capture;
            _cancellation = cancellation;
            _acceptTask = AcceptLoopAsync(listener, cancellation.Token);
            capture.StartRecording();
            SetStatus($"Capturing PC audio; listening on TCP {Port} ({_sampleRate} Hz, {_channels} ch).");
        }
        catch
        {
            capture.Dispose();
            _listener?.Stop();
            _listener = null;
            _capture = null;
            throw;
        }
    }

    public void Stop()
    {
        var cancellation = _cancellation;
        if (cancellation is null) return;

        _cancellation = null;
        cancellation.Cancel();
        _listener?.Stop();
        _listener = null;
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.StopRecording();
            _capture.Dispose();
            _capture = null;
        }
        foreach (var client in _clients.Values)
            client.Dispose();
        _clients.Clear();
        try { _acceptTask?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
        _acceptTask = null;
        cancellation.Dispose();
        SetStatus("Audio server stopped.");
    }

    private void ConfigureSourceFormat(WaveFormat format)
    {
        var encoding = format.Encoding;
        if (encoding == WaveFormatEncoding.Extensible)
        {
            if (format is not WaveFormatExtensible extensible)
                throw new NotSupportedException("The loopback audio format is extensible but its subtype is unavailable.");
            if (extensible.SubFormat == PcmSubFormat)
                encoding = WaveFormatEncoding.Pcm;
            else if (extensible.SubFormat == FloatSubFormat)
                encoding = WaveFormatEncoding.IeeeFloat;
            else
                throw new NotSupportedException($"Unsupported loopback audio subtype: {extensible.SubFormat}.");
        }

        if (format.Channels is < 1 or > 2)
            throw new NotSupportedException($"Android streaming supports mono or stereo audio; Windows reported {format.Channels} channels.");
        if (format.SampleRate is < 8000 or > 192000)
            throw new NotSupportedException($"Unsupported loopback sample rate: {format.SampleRate} Hz.");
        if (encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample != 32)
            throw new NotSupportedException($"Unsupported floating-point depth: {format.BitsPerSample} bits.");
        if (encoding != WaveFormatEncoding.IeeeFloat &&
            (encoding != WaveFormatEncoding.Pcm || format.BitsPerSample is not (8 or 16 or 24 or 32)))
            throw new NotSupportedException($"Unsupported loopback format: {encoding}, {format.BitsPerSample} bits.");

        _sampleRate = format.SampleRate;
        _channels = format.Channels;
        _sourceBits = format.BitsPerSample;
        _sourceIsFloat = encoding == WaveFormatEncoding.IeeeFloat;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        try
        {
            if (_clients.IsEmpty) return;
            var sampleBytes = _sourceBits / 8;
            var frameBytes = sampleBytes * _channels;
            var frames = args.BytesRecorded / frameBytes;
            if (args.BytesRecorded % frameBytes != 0)
                throw new InvalidDataException("Loopback capture returned a partial audio frame.");

            var pcm = new byte[frames * _channels * sizeof(short)];
            for (var sample = 0; sample < frames * _channels; sample++)
            {
                var sourceOffset = sample * sampleBytes;
                var value = ReadSample(args.Buffer, sourceOffset);
                var pcmValue = (short)Math.Round(Math.Clamp(value, -1f, 1f) * (value < 0 ? 32768f : 32767f));
                BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(sample * sizeof(short), sizeof(short)), pcmValue);
            }

            foreach (var client in _clients.Values)
                client.Frames.Writer.TryWrite(pcm);
        }
        catch (Exception ex)
        {
            SetStatus($"Audio capture failed: {ex.Message}");
        }
    }

    private float ReadSample(byte[] buffer, int offset)
    {
        if (_sourceIsFloat)
            return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset, 4)));

        return _sourceBits switch
        {
            8 => (buffer[offset] - 128) / 128f,
            16 => BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset, 2)) / 32768f,
            24 => ReadInt24(buffer, offset) / 8388608f,
            32 => BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset, 4)) / 2147483648f,
            _ => throw new NotSupportedException($"Unsupported PCM depth: {_sourceBits} bits.")
        };
    }

    private static int ReadInt24(byte[] buffer, int offset)
    {
        var value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
        return (value & 0x800000) == 0 ? value : value | unchecked((int)0xff000000);
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var socket = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                socket.NoDelay = true;
                socket.SendBufferSize = 4096;
                var id = Guid.NewGuid();
                var client = new AudioClient(socket);
                _clients[id] = client;
                _ = SendToClientAsync(id, client, cancellationToken);
                SetStatus($"Capturing PC audio; {ClientCount} Android client(s) connected.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (SocketException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            SetStatus($"Audio listener failed: {ex.Message}");
        }
    }

    private async Task SendToClientAsync(Guid id, AudioClient client, CancellationToken cancellationToken)
    {
        try
        {
            var stream = client.Socket.GetStream();
            var header = new byte[HeaderSize];
            "AUD1"u8.CopyTo(header);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), _sampleRate);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8, 2), (ushort)_channels);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10, 2), 1);
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);

            await foreach (var frame in client.Frames.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException) { }
        catch (SocketException) { }
        finally
        {
            _clients.TryRemove(id, out _);
            client.Dispose();
            if (_capture is not null)
                SetStatus($"Capturing PC audio; {ClientCount} Android client(s) connected.");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is not null)
            SetStatus($"Audio capture stopped with an error: {args.Exception.Message}");
    }

    private void SetStatus(string status)
    {
        _status = status;
        _statusChanged(status);
    }

    public void Dispose() => Stop();

    private sealed class AudioClient(TcpClient socket) : IDisposable
    {
        public TcpClient Socket { get; } = socket;
        public Channel<byte[]> Frames { get; } = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(1) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropOldest });

        public void Dispose()
        {
            Frames.Writer.TryComplete();
            Socket.Dispose();
        }
    }

    private sealed class LowLatencyLoopbackCapture : WasapiCapture
    {
        public LowLatencyLoopbackCapture()
            : base(WasapiLoopbackCapture.GetDefaultLoopbackCaptureDevice(), true, CaptureBufferMilliseconds)
        {
        }

        protected override AudioClientStreamFlags GetAudioClientStreamFlags() =>
            AudioClientStreamFlags.Loopback;
    }
}
