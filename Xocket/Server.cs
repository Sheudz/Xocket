using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace Xocket
{
    public class XocketServer
    {
        private TcpListener _tcpListener;
        private bool _isRunning;
        public int BufferSize { get; private set; } = 1024;
        private static Dictionary<string, byte[]> PendingPackets = new Dictionary<string, byte[]>();
        private static Dictionary<string, Tuple<byte[], TcpClient>> CompletedPackets = new Dictionary<string, Tuple<byte[], TcpClient>>();

        private static readonly ConditionalWeakTable<TcpClient, ClientEventHandlers> ClientEventTable =
            new ConditionalWeakTable<TcpClient, ClientEventHandlers>();

        private List<CancellationTokenSource> _activeListeners = new List<CancellationTokenSource>();

        public void StartServer(int port)
        {
            if (port < 0 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port), "Invalid port.");
            }

            if (_isRunning)
            {
                throw new InvalidOperationException("Server is already running.");
            }

            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, port);
                _tcpListener.Start();
                _isRunning = true;

                _ = ListenForConnectionsAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to start server: {ex.Message}", ex);
            }
        }

        public void StopServer()
        {
            if (!_isRunning)
            {
                throw new InvalidOperationException("Server is not running.");
            }

            try
            {
                _isRunning = false;
                _tcpListener.Stop();

                foreach (var listener in _activeListeners)
                {
                    listener.Cancel();
                }

                _activeListeners.Clear();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to stop server: {ex.Message}", ex);
            }
        }

        public void SetBufferSize(int? size)
        {
            if (size < 64)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "Buffer size is too small.");
            }
            else if (size > 4096)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "Buffer size is too large.");
            }

            BufferSize = size ?? 1024;
        }

        public Action Listen(string? packetId = null, TcpClient? specificClient = null, Func<TcpClient, byte[], Task> callback = null)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            var token = cancellationTokenSource.Token;

            _activeListeners.Add(cancellationTokenSource);

            Task.Run(async () =>
            {
                try
                {
                    while (_isRunning && !token.IsCancellationRequested)
                    {
                        foreach (KeyValuePair<string, Tuple<byte[], TcpClient>> packetEntry in CompletedPackets)
                        {
                            Tuple<byte[], TcpClient> packet = packetEntry.Value;

                            bool idMatches = packetId == null || packetEntry.Key == packetId;
                            bool clientMatches = specificClient == null || packet.Item2 == specificClient;

                            if (idMatches && clientMatches)
                            {
                                if (callback != null)
                                {
                                    await callback.Invoke(packet.Item2, packet.Item1);
                                }
                                CompletedPackets.Remove(packetEntry.Key);
                                break;
                            }
                        }
                        await Task.Delay(15);
                    }
                }
                catch { }
            });

            return new Action(() =>
            {
                cancellationTokenSource.Cancel();
                _activeListeners.Remove(cancellationTokenSource);
            });
        }

        public void StopListening(Action stopListenAction)
        {
            stopListenAction();
        }

        public async Task SendMessage(TcpClient client, string? packetId, byte[] messageBytes)
        {
            if (client == null || !client.Connected)
            {
                throw new InvalidOperationException("Connection lost.");
            }

            if (packetId != null && Encoding.UTF8.GetBytes(packetId).Length > BufferSize * 0.30)
            {
                throw new ArgumentException("Packet ID is too long.", nameof(packetId));
            }
            try
            {
                byte[] header = Encoding.UTF8.GetBytes($"singlemessage¶|~{packetId ?? "nullid"}¶|~");
                if (4 + messageBytes.Length + header.Length > BufferSize)
                {
                    NetworkStream stream = client.GetStream();
                    string dataId = Guid.NewGuid().ToString();
                    string startMessage = $"{Encoding.UTF8.GetBytes($"startlistening¶|~{dataId}").Length.ToString("D4")}startlistening¶|~{dataId}";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(startMessage), 0, Encoding.UTF8.GetBytes(startMessage).Length);
                    int chunkSize = BufferSize - Encoding.UTF8.GetBytes($"appenddata¶|~{dataId}¶|~").Length - 4;
                    int bytesSent = 0;

                    while (bytesSent < messageBytes.Length)
                    {
                        int bytesToSend = Math.Min(chunkSize, messageBytes.Length - bytesSent);
                        byte[] chunkHeader = Encoding.UTF8.GetBytes($"appenddata¶|~{dataId}¶|~");
                        byte[] chunk = chunkHeader.Concat(messageBytes.Skip(bytesSent).Take(bytesToSend)).ToArray();
                        byte[] chunkLength = Encoding.UTF8.GetBytes(chunk.Length.ToString("D4"));
                        byte[] appendMessage = chunkLength.Concat(chunk).ToArray();
                        await stream.WriteAsync(appendMessage, 0, appendMessage.Length);
                        bytesSent += bytesToSend;
                    }

                    string endMessage = Encoding.UTF8.GetBytes($"enddata¶|~{dataId}¶|~{packetId ?? "nullid"}").Length.ToString("D4") + $"enddata¶|~{dataId}¶|~{packetId ?? "nullid"}";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(endMessage), 0, Encoding.UTF8.GetBytes(endMessage).Length);
                }
                else
                {
                    int size = 4 + header.Length + messageBytes.Length;
                    byte[] sizeHeader = Encoding.UTF8.GetBytes(size.ToString("D4"));
                    byte[] fullMessage = sizeHeader.Concat(header).Concat(messageBytes).ToArray();
                    NetworkStream stream = client.GetStream();
                    await stream.WriteAsync(fullMessage, 0, fullMessage.Length);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to send message: {ex.Message}", ex);
            }
        }

        private async Task ListenForConnectionsAsync()
        {
            try
            {
                while (_isRunning)
                {
                    TcpClient client = await _tcpListener.AcceptTcpClientAsync();
                    client.NoDelay = true;
                    _ = HandleClientAsync(client);
                }
            } catch { }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[BufferSize];

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = cancellationTokenSource.Token;

            try
            {
                _ = MonitorClientConnectionAsync(client, cancellationTokenSource);

                while (_isRunning && !token.IsCancellationRequested)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, 4, token);
                    if (bytesRead == 0) break;

                    string messageSizeStr = Encoding.UTF8.GetString(buffer, 0, 4);
                    if (!int.TryParse(messageSizeStr, out int messageSize)) continue;
                    if (messageSize <= 0 || messageSize > BufferSize) continue;

                    bytesRead = await stream.ReadAsync(buffer, 0, messageSize, token);
                    if (bytesRead == 0) break;

                    byte[] receivedBytes = buffer.Take(bytesRead).ToArray();

                    try
                    {
                        string header = Encoding.UTF8.GetString(receivedBytes, 0, Math.Min(receivedBytes.Length, BufferSize));
                        string[] messageParts = header.Split(new string[] { "¶|~" }, StringSplitOptions.None);

                        if (messageParts.Length > 0)
                        {
                            if (messageParts[0] == "singlemessage")
                            {
                                string packetId = messageParts[1];
                                int headerLength = Encoding.UTF8.GetBytes($"singlemessage¶|~{packetId}¶|~").Length;

                                byte[] payloadBytes = receivedBytes.Skip(headerLength).ToArray();
                                CompletedPackets[packetId] = Tuple.Create(payloadBytes, client);
                            }
                            else if (messageParts[0] == "startlistening")
                            {
                                string dataId = messageParts[1];
                                PendingPackets[dataId] = Array.Empty<byte>();
                            }
                            else if (messageParts[0] == "appenddata")
                            {
                                string dataId = messageParts[1];
                                if (PendingPackets.TryGetValue(dataId, out byte[] existingData))
                                {
                                    int headerLength = Encoding.UTF8.GetBytes($"appenddata¶|~{dataId}¶|~").Length;
                                    byte[] payloadBytes = receivedBytes.Skip(headerLength).ToArray();

                                    PendingPackets[dataId] = existingData.Concat(payloadBytes).ToArray();
                                }
                            }
                            else if (messageParts[0] == "enddata")
                            {
                                string dataId = messageParts[1];
                                if (PendingPackets.TryGetValue(dataId, out byte[] finalData))
                                {
                                    string packetId = messageParts[2];
                                    CompletedPackets[packetId] = Tuple.Create(finalData, client);
                                    PendingPackets.Remove(dataId);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            finally
            {
                cancellationTokenSource.Cancel();
                client.Close();
                if (ClientEventTable.TryGetValue(client, out var handlers))
                {
                    handlers.InvokeOnDisconnect();
                }
            }
        }

        private async Task MonitorClientConnectionAsync(TcpClient client, CancellationTokenSource cancellationTokenSource)
        {
            try
            {
                while (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    if (!client.Connected)
                    {
                        cancellationTokenSource.Cancel();
                        break;
                    }
                    await Task.Delay(1000);
                }
            }
            catch
            {
                cancellationTokenSource.Cancel();
            }
        }

        public void OnDisconnect(TcpClient client, Action callback)
        {
            if (client != null)
            {
                ClientEventTable.AddOrUpdate(client, new ClientEventHandlers(callback));
            }
        }

        private class ClientEventHandlers
        {
            private Action _onDisconnect;

            public ClientEventHandlers(Action onDisconnect)
            {
                _onDisconnect = onDisconnect;
            }

            public void InvokeOnDisconnect()
            {
                _onDisconnect?.Invoke();
            }
        }
    }
}
