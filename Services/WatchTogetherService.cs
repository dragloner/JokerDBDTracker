using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace JokerDBDTracker.Services
{
    /// <summary>
    /// Lightweight TCP-based Watch Together service for LAN / Radmin VPN.
    /// The host runs a <see cref="TcpListener"/>; guests connect via <see cref="TcpClient"/>.
    /// Messages are length-prefixed UTF-8 JSON packets.
    /// </summary>
    public sealed class WatchTogetherService : IDisposable
    {
        public const int DefaultPort = 7777;
        private const int MaxMessageBytes = 8192;
        private const int MaxPeers = 10;
        private const int ConnectTimeoutMs = 5000;

        // ── Events (always raised on the caller's SynchronizationContext) ──
        public event Action<WatchTogetherMessage>? MessageReceived;
        public event Action<string>? PeerConnected;
        public event Action<string>? PeerDisconnected;
        public event Action<string>? Error;
        public event Action? Stopped;

        private TcpListener? _listener;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private SemaphoreSlim? _guestWriteLock;
        private CancellationTokenSource? _cts;
        private readonly SynchronizationContext? _syncContext;
        private readonly List<ConnectedPeer> _peers = [];
        private readonly Lock _peersLock = new();
        private bool _disposed;

        public bool IsHost { get; private set; }
        public bool IsConnected { get; private set; }
        public int PeerCount
        {
            get
            {
                lock (_peersLock)
                {
                    return _peers.Count;
                }
            }
        }

        public List<string> GetPeerEndpoints()
        {
            lock (_peersLock)
            {
                return _peers.Select(p => p.Endpoint).ToList();
            }
        }

        public WatchTogetherService()
        {
            _syncContext = SynchronizationContext.Current;
        }

        // ── HOST ──

        public void StartHost(int port = DefaultPort)
        {
            StopInternal(fireEvent: false);
            IsHost = true;
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            IsConnected = true;
            _ = AcceptClientsAsync(_cts.Token);
        }

        private async Task AcceptClientsAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var client = await _listener!.AcceptTcpClientAsync(ct);
                    var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";

                    // Reject if at max capacity.
                    lock (_peersLock)
                    {
                        if (_peers.Count >= MaxPeers)
                        {
                            client.Close();
                            continue;
                        }
                    }

                    var peer = new ConnectedPeer(client, endpoint);
                    lock (_peersLock)
                    {
                        _peers.Add(peer);
                    }

                    Post(() => PeerConnected?.Invoke(endpoint));
                    _ = ReadPeerMessagesAsync(peer, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Post(() => Error?.Invoke(ex.Message));
            }
        }

        private async Task ReadPeerMessagesAsync(ConnectedPeer peer, CancellationToken ct)
        {
            try
            {
                var stream = peer.Client.GetStream();
                while (!ct.IsCancellationRequested && peer.Client.Connected)
                {
                    var message = await ReadMessageAsync(stream, ct);
                    if (message is null)
                    {
                        break;
                    }

                    // Forward to other peers (relay).
                    BroadcastToPeers(message, excludeEndpoint: peer.Endpoint);
                    Post(() => MessageReceived?.Invoke(message));
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch
            {
                // Peer disconnected.
            }
            finally
            {
                RemovePeer(peer);
                Post(() => PeerDisconnected?.Invoke(peer.Endpoint));
            }
        }

        // ── GUEST ──

        public async Task ConnectAsync(string host, int port = DefaultPort)
        {
            StopInternal(fireEvent: false);
            IsHost = false;
            _cts = new CancellationTokenSource();
            _client = new TcpClient();

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            connectCts.CancelAfter(ConnectTimeoutMs);
            try
            {
                await _client.ConnectAsync(host, port, connectCts.Token);
            }
            catch (OperationCanceledException) when (!_cts.Token.IsCancellationRequested)
            {
                _client.Dispose();
                _client = null;
                throw new TimeoutException($"Connection timed out after {ConnectTimeoutMs / 1000}s");
            }

            _stream = _client.GetStream();
            _guestWriteLock = new SemaphoreSlim(1, 1);
            IsConnected = true;
            Post(() => PeerConnected?.Invoke(host));
            _ = ReadGuestMessagesAsync(_cts.Token);
        }

        private async Task ReadGuestMessagesAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _stream is not null)
                {
                    var message = await ReadMessageAsync(_stream, ct);
                    if (message is null)
                    {
                        break;
                    }

                    Post(() => MessageReceived?.Invoke(message));
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch
            {
                // Host disconnected.
            }
            finally
            {
                IsConnected = false;
                Post(() =>
                {
                    PeerDisconnected?.Invoke("host");
                    Stopped?.Invoke();
                });
            }
        }

        // ── SEND ──

        public void Send(WatchTogetherMessage message)
        {
            if (!IsConnected)
            {
                return;
            }

            var bytes = SerializeMessage(message);
            if (IsHost)
            {
                BroadcastToPeersRaw(bytes, excludeEndpoint: null);
            }
            else if (_stream is not null && _guestWriteLock is not null)
            {
                _ = WriteWithLockAsync(_stream, _guestWriteLock, bytes);
            }
        }

        private void BroadcastToPeers(WatchTogetherMessage message, string? excludeEndpoint)
        {
            var bytes = SerializeMessage(message);
            BroadcastToPeersRaw(bytes, excludeEndpoint);
        }

        private void BroadcastToPeersRaw(byte[] bytes, string? excludeEndpoint)
        {
            List<ConnectedPeer> snapshot;
            lock (_peersLock)
            {
                snapshot = [.. _peers];
            }

            foreach (var peer in snapshot)
            {
                if (excludeEndpoint is not null &&
                    string.Equals(peer.Endpoint, excludeEndpoint, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    var stream = peer.Client.GetStream();
                    _ = WriteWithLockAsync(stream, peer.WriteLock, bytes);
                }
                catch
                {
                    RemovePeer(peer);
                }
            }
        }

        // ── STOP ──

        public void Stop()
        {
            StopInternal(fireEvent: true);
        }

        private void StopInternal(bool fireEvent)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            _stream = null;
            _guestWriteLock?.Dispose();
            _guestWriteLock = null;
            _client?.Close();
            _client?.Dispose();
            _client = null;

            _listener?.Stop();
            _listener = null;

            lock (_peersLock)
            {
                foreach (var peer in _peers)
                {
                    try
                    {
                        peer.Client.Close();
                        peer.Dispose();
                    }
                    catch
                    {
                        // Ignore.
                    }
                }

                _peers.Clear();
            }

            IsHost = false;
            IsConnected = false;

            if (fireEvent)
            {
                Post(() => Stopped?.Invoke());
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopInternal(fireEvent: false);
        }

        // ── Protocol helpers ──

        private static byte[] SerializeMessage(WatchTogetherMessage message)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(message);
            var length = BitConverter.GetBytes(json.Length);
            var packet = new byte[4 + json.Length];
            Buffer.BlockCopy(length, 0, packet, 0, 4);
            Buffer.BlockCopy(json, 0, packet, 4, json.Length);
            return packet;
        }

        private static async Task<WatchTogetherMessage?> ReadMessageAsync(NetworkStream stream, CancellationToken ct)
        {
            var lengthBuf = new byte[4];
            var read = 0;
            while (read < 4)
            {
                var chunk = await stream.ReadAsync(lengthBuf.AsMemory(read, 4 - read), ct);
                if (chunk == 0)
                {
                    return null;
                }

                read += chunk;
            }

            var messageLength = BitConverter.ToInt32(lengthBuf, 0);
            if (messageLength <= 0 || messageLength > MaxMessageBytes)
            {
                return null;
            }

            var messageBuf = new byte[messageLength];
            read = 0;
            while (read < messageLength)
            {
                var chunk = await stream.ReadAsync(messageBuf.AsMemory(read, messageLength - read), ct);
                if (chunk == 0)
                {
                    return null;
                }

                read += chunk;
            }

            return JsonSerializer.Deserialize<WatchTogetherMessage>(messageBuf);
        }

        private static async Task WriteWithLockAsync(NetworkStream stream, SemaphoreSlim writeLock, byte[] data)
        {
            try
            {
                await writeLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    await stream.WriteAsync(data).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
                finally
                {
                    writeLock.Release();
                }
            }
            catch
            {
                // Best-effort send.
            }
        }

        private void RemovePeer(ConnectedPeer peer)
        {
            lock (_peersLock)
            {
                _peers.Remove(peer);
            }

            try
            {
                peer.Client.Close();
            }
            catch
            {
                // Ignore.
            }
        }

        private void Post(Action action)
        {
            if (_syncContext is not null)
            {
                _syncContext.Post(_ => action(), null);
            }
            else
            {
                action();
            }
        }

        private sealed class ConnectedPeer(TcpClient client, string endpoint) : IDisposable
        {
            public TcpClient Client { get; } = client;
            public string Endpoint { get; } = endpoint;
            public SemaphoreSlim WriteLock { get; } = new(1, 1);

            public void Dispose()
            {
                WriteLock.Dispose();
            }
        }
    }

    // ── Message model ──

    public sealed class WatchTogetherMessage
    {
        public string Type { get; set; } = string.Empty;
        public double? Position { get; set; }
        public string? VideoId { get; set; }
        public string? VideoTitle { get; set; }
        public string? Text { get; set; }
        public string? SenderName { get; set; }
    }
}
