using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Xocket
{
    public class XocketClient
    {
        private TcpClient _tcpClient;
        private NetworkStream _networkStream;
        private bool _isConnected;
        public int BufferSize = 1024;
        private static Dictionary<string, string> PendingPackets = new Dictionary<string, string>();
        private static Dictionary<string, string> CompletedPackets = new Dictionary<string, string>();

        public string Connect(string hostname, int port)
        {
            if (_isConnected) return "Already connected.";

            try
            {
                _tcpClient = new TcpClient();
                _tcpClient.Connect(hostname, port);
                _networkStream = _tcpClient.GetStream();
                _isConnected = true;

                _ = ListenForMessages();
                return "Connected successfully.";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
        public string Disconnect()
        {
            if (!_isConnected) return "Not connected.";

            _isConnected = false;
            _networkStream?.Close();
            _tcpClient?.Close();
            return "Disconnected successfully.";
        }
        public async Task<string> SendMessage(string? packetId, string message)
        {
            if (!_isConnected || _tcpClient == null || !_tcpClient.Connected) return "Not connected.";

            if (packetId != null && Encoding.UTF8.GetBytes(packetId).Length > BufferSize * 0.25)
            {
                return "Packet ID is too long.";
            }

            try
            {
                byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                byte[] header = Encoding.UTF8.GetBytes($"singlemessage¶|~{packetId ?? "nullid"}¶|~");
                if (messageBytes.Length + header.Length > BufferSize)
                {
                    string dataId = Guid.NewGuid().ToString();
                    string startMessage = $"startlistening¶|~{dataId}¶|~{packetId ?? "nullid"}";
                    await _networkStream.WriteAsync(Encoding.UTF8.GetBytes(startMessage), 0, Encoding.UTF8.GetBytes(startMessage).Length);

                    int chunkSize = BufferSize - Encoding.UTF8.GetBytes($"appenddata¶|~{dataId}¶|~").Length;
                    int bytesSent = 0;

                    while (bytesSent < messageBytes.Length)
                    {
                        int bytesToSend = Math.Min(chunkSize, messageBytes.Length - bytesSent);
                        byte[] chunk = Encoding.UTF8.GetBytes($"appenddata¶|~{dataId}¶|~")
                            .Concat(messageBytes.Skip(bytesSent).Take(bytesToSend))
                            .ToArray();

                        await _networkStream.WriteAsync(chunk, 0, chunk.Length);
                        bytesSent += bytesToSend;
                    }

                    string endMessage = $"enddata¶|~{dataId}";
                    await _networkStream.WriteAsync(Encoding.UTF8.GetBytes(endMessage), 0, Encoding.UTF8.GetBytes(endMessage).Length);

                    return "Message sent successfully.";
                }
                else
                {
                    byte[] fullMessage = Encoding.UTF8.GetBytes($"singlemessage¶|~{packetId ?? "nullid"}¶|~{message}");
                    await _networkStream.WriteAsync(fullMessage, 0, fullMessage.Length);
                    return "Message sent successfully.";
                }
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
        public async Task Listen(Func<string, string, Task> callback)
        {
            while (_isConnected)
            {
                foreach (KeyValuePair<string, string> packetEntry in CompletedPackets.ToList())
                {
                    string message = packetEntry.Value;
                    string server = packetEntry.Key;

                    if (callback != null)
                    {
                        await callback.Invoke(server, message);
                    }

                    CompletedPackets.Remove(packetEntry.Key);
                    break;
                }
                await Task.Delay(100);
            }
        }
        private async Task ListenForMessages()
        {
            byte[] buffer = new byte[BufferSize];

            try
            {
                while (_isConnected)
                {
                    int bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length);
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
                                CompletedPackets[packetId] = payload;
                            }
                            else if (messageParts[0] == "startlistening")
                            {
                                string dataId = messageParts[1];
                                string packetId = messageParts[2];
                                PendingPackets[dataId] = "";
                            }
                            else if (messageParts[0] == "appenddata")
                            {
                                string dataId = messageParts[1];
                                if (PendingPackets.ContainsKey(dataId))
                                {
                                    PendingPackets[dataId] += messageParts[2];
                                }
                            }
                            else if (messageParts[0] == "enddata")
                            {
                                string dataId = messageParts[1];
                                if (PendingPackets.TryGetValue(dataId, out string payload))
                                {
                                    string packetId = messageParts[2];
                                    CompletedPackets[packetId] = payload;
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
                Disconnect();
            }
        }
    }
}
