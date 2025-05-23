﻿using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace Xocket
{
    public class XocketServer
    {
        private TcpListener _tcpListener;
        private bool _isRunning;
        public int BufferSize { get; private set; } = 1024;
        private static Dictionary<string, (int, byte[])> PendingPackets = new Dictionary<string, (int, byte[])>();
        private static Dictionary<string, Tuple<byte[], TcpClient>> CompletedPackets = new Dictionary<string, Tuple<byte[], TcpClient>>();

        private static readonly ConditionalWeakTable<TcpClient, ClientEventHandlers> ClientEventTable =
            new ConditionalWeakTable<TcpClient, ClientEventHandlers>();
        public event Action<TcpClient> OnClientConnected;
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
                    stream.WriteAsync(Encoding.UTF8.GetBytes(startMessage), 0, Encoding.UTF8.GetBytes(startMessage).Length);
                    int chunkSize = BufferSize - Encoding.UTF8.GetBytes($"appenddata¶|~{dataId}¶|~").Length - 4;
                    int bytesSent = 0;

                    while (bytesSent < messageBytes.Length)
                    {
                        int bytesToSend = Math.Min(chunkSize, messageBytes.Length - bytesSent);
                        byte[] chunkHeader = Encoding.UTF8.GetBytes($"appenddata¶|~{dataId}¶|~");
                        byte[] chunk = chunkHeader.Concat(messageBytes.Skip(bytesSent).Take(bytesToSend)).ToArray();
                        byte[] chunkLength = Encoding.UTF8.GetBytes(chunk.Length.ToString("D4"));
                        byte[] appendMessage = chunkLength.Concat(chunk).ToArray();
                        stream.WriteAsync(appendMessage, 0, appendMessage.Length);
                        bytesSent += bytesToSend;
                    }

                    string endMessage = Encoding.UTF8.GetBytes($"enddata¶|~{dataId}¶|~{packetId ?? "nullid"}").Length.ToString("D4") + $"enddata¶|~{dataId}¶|~{packetId ?? "nullid"}";
                    stream.WriteAsync(Encoding.UTF8.GetBytes(endMessage), 0, Encoding.UTF8.GetBytes(endMessage).Length);
                }
                else
                {
                    int size = header.Length + messageBytes.Length;
                    byte[] sizeHeader = Encoding.UTF8.GetBytes(size.ToString("D4"));
                    byte[] fullMessage = sizeHeader.Concat(header).Concat(messageBytes).ToArray();
                    NetworkStream stream = client.GetStream();
                    stream.WriteAsync(fullMessage, 0, fullMessage.Length);
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
                    OnClientConnected?.Invoke(client);
                    _ = HandleClientAsync(client);
                }
            }
            catch { }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            using (MemoryStream memoryStream = new MemoryStream())
            {
                CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
                CancellationToken token = cancellationTokenSource.Token;

                try
                {
                    _ = MonitorClientConnectionAsync(client, cancellationTokenSource);

                    while (_isRunning && !token.IsCancellationRequested)
                    {
                        byte[] headerBuffer = new byte[4];
                        int headerBytesRead = 0;

                        while (headerBytesRead < 4)
                        {
                            int bytesRead = await stream.ReadAsync(headerBuffer, headerBytesRead, 4 - headerBytesRead, token);
                            if (bytesRead == 0) break;

                            headerBytesRead += bytesRead;
                        }

                        if (headerBytesRead < 4) break;

                        string messageSizeStr = Encoding.UTF8.GetString(headerBuffer, 0, 4);
                        if (!int.TryParse(messageSizeStr, out int messageSize)) continue;
                        if (messageSize <= 0 || messageSize > BufferSize) continue;

                        int totalBytesRead = 0;
                        byte[] messageBuffer = new byte[messageSize];

                        while (totalBytesRead < messageSize)
                        {
                            int bytesRead = await stream.ReadAsync(messageBuffer, totalBytesRead, messageSize - totalBytesRead, token);
                            if (bytesRead == 0) break;

                            totalBytesRead += bytesRead;
                        }

                        if (totalBytesRead < messageSize) break;

                        memoryStream.Write(messageBuffer, 0, totalBytesRead);

                        memoryStream.Seek(0, SeekOrigin.Begin);
                        byte[] receivedBytes = memoryStream.ToArray();
                        memoryStream.SetLength(0);

                        try
                        {
                            string header = Encoding.UTF8.GetString(receivedBytes, 0, Math.Min(receivedBytes.Length, BufferSize));
                            string[] messageParts = header.Split(new string[] { "\u00b6|~" }, StringSplitOptions.None);
                            if (messageParts.Length > 0)
                            {
                                if (messageParts[0] == "singlemessage")
                                {
                                    string packetId = messageParts[1];
                                    int headerLength = Encoding.UTF8.GetBytes($"singlemessage\u00b6|~{packetId}\u00b6|~").Length;

                                    byte[] payloadBytes = receivedBytes.Skip(headerLength).ToArray();

                                    CompletedPackets[packetId] = Tuple.Create(payloadBytes, client);

                                }
                                else if (messageParts[0] == "startlistening")
                                {
                                    string dataId = messageParts[1];

                                    PendingPackets[dataId] = (0, new byte[int.Parse(messageParts[2])]);

                                }

                                else if (messageParts[0] == "appenddata")
                                {
                                    string dataId = messageParts[1];

                                    if (PendingPackets.TryGetValue(dataId, out var packetData))
                                    {
                                        int writtenBytes = packetData.Item1;
                                        byte[] dataArray = packetData.Item2;

                                        int headerLength = Encoding.UTF8.GetBytes($"appenddata\u00b6|~{dataId}\u00b6|~").Length;
                                        byte[] payloadBytes = receivedBytes.Skip(headerLength).ToArray();

                                        if (writtenBytes + payloadBytes.Length <= dataArray.Length)
                                        {
                                            Array.Copy(payloadBytes, 0, dataArray, writtenBytes, payloadBytes.Length);
                                            PendingPackets[dataId] = (writtenBytes + payloadBytes.Length, dataArray);
                                        }
                                    }
                                }
                                else if (messageParts[0] == "enddata")
                                {
                                    string dataId = messageParts[1];
                                    if (PendingPackets.TryGetValue(dataId, out var packetData))
                                    {
                                        string packetId = messageParts[2];

                                        byte[] finalData = packetData.Item2;

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

        public void OnConnect(Action<TcpClient> callback)
        {
            OnClientConnected += callback;
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