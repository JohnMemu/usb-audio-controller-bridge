using System.Net;
using System.Net.Sockets;

namespace UsbAudioController;

internal sealed class PcDiscoveryServer : IDisposable
{
    private const int Port = 17052;
    private static readonly byte[] Request = "USBC-DISCOVER-1"u8.ToArray();
    private static readonly byte[] Response = "USBC-HOST-1"u8.ToArray();

    private UdpClient? _udp;
    private CancellationTokenSource? _cancellation;
    private Task? _receiveTask;

    public void Start()
    {
        if (_udp is not null) return;

        var udp = new UdpClient(new IPEndPoint(IPAddress.Any, Port));
        try
        {
            var cancellation = new CancellationTokenSource();
            _udp = udp;
            _cancellation = cancellation;
            _receiveTask = ReceiveLoopAsync(udp, cancellation.Token);
        }
        catch
        {
            udp.Dispose();
            throw;
        }
    }

    private static async Task ReceiveLoopAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (!result.Buffer.AsSpan().SequenceEqual(Request)) continue;
                await udp.SendAsync(Response, result.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (SocketException) when (cancellationToken.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        var cancellation = _cancellation;
        if (cancellation is null) return;

        _cancellation = null;
        cancellation.Cancel();
        _udp?.Dispose();
        _udp = null;
        try
        {
            _receiveTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { }
        _receiveTask = null;
        cancellation.Dispose();
    }
}
