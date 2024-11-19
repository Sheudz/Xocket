using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;

namespace Xocket
{
    public class XocketServer
    {
        private TcpListener _tcpListener;
        private bool _isRunning;
        public int BufferSize { get; private set; } = 1024;
        private static Dictionary<string, string[]> PendingPackets = new Dictionary<string, string[]>();
        private static Dictionary<string, Tuple<string, TcpClient>> CompletedPackets = new Dictionary<string, Tuple<string, TcpClient>>();
        private static Dictionary<TcpClient, Action> ClientDisconnectCallbacks = new Dictionary<TcpClient, Action>();

        public string StartServer(int port)
        {
            if (port < 0 || port > 65535)
            {
                return "Invalid port.";
            }
            if (_isRunning) return "Server is already running.";

            _tcpListener = new TcpListener(IPAddress.Any, port);
            _tcpListener.Start();
            _isRunning = true;

            _ = ListenForConnectionsAsync();
            return "Server started successfully.";
        }

        public string StopServer()
        {
            if (!_isRunning) return "Server is not running.";

            _isRunning = false;
            _tcpListener.Stop();
            return "Server stopped successfully.";
        }
        public string SetBufferSize(int? size)
        {
            if (BufferSize < 64)
            {
                return "Buffer size is too small.";
            }
            else if (BufferSize > 4096)
            {
                return "Buffer size is too large.";
            }
            BufferSize = size ?? 1024;
            return "successfully.";
        }

        public async Task Listen(string? packetId = null, TcpClient? specificClient = null, Func<TcpClient, string, Task> callback = null)
        {
            while (_isRunning)
            {
                foreach (KeyValuePair<string, Tuple<string, TcpClient>> packetEntry in CompletedPackets)
                {
                    Tuple<string, TcpClient> packet = packetEntry.Value;

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
                await Task.Delay(100);
            }
        }

        public async Task<string> SendMessage(TcpClient client, string? packetId, string message)
        {
            if (client == null || !client.Connected) return "Connection lost.";
            if (packetId != null && Encoding.UTF8.GetBytes(packetId).Length > BufferSize * 0.30)
            {
                return "Packet ID is too long.";
            }
            try
            {
                byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                byte[] header = Encoding.UTF8.GetBytes($"singlemessage¶|~{packetId ?? "nullid"}¶|~");
                if (4 + messageBytes.Length + header.Length > BufferSize)
                {
                    NetworkStream stream = client.GetStream();
                    string dataId = Guid.NewGuid().ToString();
                    string startMessage = $"{Encoding.UTF8.GetBytes($"startlistening¶|~{dataId}¶|~{packetId ?? "nullid"}").Length.ToString("D4")}startlistening¶|~{dataId}¶|~{packetId ?? "nullid"}";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(startMessage), 0, Encoding.UTF8.GetBytes(startMessage).Length);

                    int chunkSize = BufferSize - Encoding.UTF8.GetBytes($"appenddata¶|~{dataId}¶|~").Length - 4;
                    int bytesSent = 0;

                    while (bytesSent < messageBytes.Length)
                    {
                        int bytesToSend = Math.Min(chunkSize, messageBytes.Length - bytesSent);
                        byte[] chunk = Encoding.UTF8.GetBytes($"appenddata¶|~{dataId}¶|~")
                            .Concat(messageBytes.Skip(bytesSent).Take(bytesToSend))
                            .ToArray();
                        byte[] chunkLength = Encoding.UTF8.GetBytes(chunk.Length.ToString("D4"));
                        byte[] appendmessage = chunkLength.Concat(chunk).ToArray();
                        await stream.WriteAsync(appendmessage, 0, appendmessage.Length);
                        bytesSent += bytesToSend;
                    }

                    string endMessage = Encoding.UTF8.GetBytes($"enddata¶|~{dataId}").Length.ToString("D4") + $"enddata¶|~{dataId}";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(endMessage), 0, Encoding.UTF8.GetBytes(endMessage).Length);

                    return "Message sent successfully.";
                }
                else
                {
                    int size = Encoding.UTF8.GetBytes($"singlemessage¶|~{packetId ?? "nullid"}¶|~{message}").Length;
                    byte[] fullMessage = Encoding.UTF8.GetBytes($"{size.ToString("D4")}singlemessage¶|~{packetId ?? "nullid"}¶|~{message}");
                    NetworkStream stream = client.GetStream();
                    await stream.WriteAsync(fullMessage, 0, fullMessage.Length);
                    return "Message sent successfully.";
                }
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
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
            }
            catch { }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[BufferSize];

            try
            {
                while (_isRunning)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, 4);
                    if (bytesRead == 0) break;

                    string messageSizeStr = Encoding.UTF8.GetString(buffer, 0, 4);
                    if (!int.TryParse(messageSizeStr, out int messageSize)) continue;
                    if (messageSize <= 0 || messageSize > BufferSize) continue;

                    bytesRead = await stream.ReadAsync(buffer, 0, messageSize);
                    if (bytesRead == 0) break;

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    try
                    {
                        string[] messageParts = message.Split(new string[] { "¶|~" }, StringSplitOptions.None);
                        if (messageParts.Length > 0)
                        {
                            if (messageParts[0] == "singlemessage")
                            {
                                string packetId = messageParts[1];
                                string payload = messageParts[2];
                                CompletedPackets[packetId] = Tuple.Create(payload, client);
                            }
                            else if (messageParts[0] == "startlistening")
                            {
                                string dataId = messageParts[1];
                                string packetId = messageParts[2];
                                PendingPackets[dataId] = new string[] { dataId, packetId, "" };
                            }
                            else if (messageParts[0] == "appenddata")
                            {
                                string dataId = messageParts[1];
                                if (PendingPackets.TryGetValue(dataId, out string[] packet))
                                {
                                    PendingPackets[dataId][2] += messageParts[2];
                                }
                            }
                            else if (messageParts[0] == "enddata")
                            {
                                string dataId = messageParts[1];
                                if (PendingPackets.TryGetValue(dataId, out string[] packet))
                                {
                                    string packetId = packet[1];
                                    string payload = packet[2];
                                    CompletedPackets[packetId] = Tuple.Create(payload, client);
                                    PendingPackets.Remove(dataId);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                client.Close();
                if (ClientDisconnectCallbacks.ContainsKey(client))
                {
                    ClientDisconnectCallbacks[client]?.Invoke();
                    ClientDisconnectCallbacks.Remove(client);
                }
            }
        }

        public void OnDisconnect(TcpClient client, Action callback)
        {
            if (client != null)
            {
                ClientDisconnectCallbacks[client] = callback;
            }
        }
    }
}
