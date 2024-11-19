using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Xocket
{
    public class XocketClient
    {
        public int BufferSize { get; private set; } = 1024;
        private TcpClient _client;
        private NetworkStream _stream;
        private static Dictionary<string, string[]> PendingPackets = new Dictionary<string, string[]>();
        private static Dictionary<string, string> CompletedPackets = new Dictionary<string, string>();
        private bool _isRunning = true;

        public string Connect(string host, int port)
        {
            try
            {
                _client = new TcpClient();
                _client.Connect(host, port);
                _stream = _client.GetStream();
                StartListening();
                return "Connection established successfully.";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        public string Disconnect()
        {
            try
            {
                if (_stream != null)
                    _stream.Close();

                if (_client != null)
                    _client.Close();

                return "Disconnected successfully.";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
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

        public async Task<string> SendMessage(string? packetId, string message)
        {
            if (_client == null || !_client.Connected) return "Connection lost.";
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
                    byte[] startMessageBytes = Encoding.UTF8.GetBytes(startMessage);

                    await _stream.WriteAsync(startMessageBytes, 0, startMessageBytes.Length);

                    int chunkSize = BufferSize - Encoding.UTF8.GetBytes($"appenddata¶|~{dataId}¶|~").Length;
                    int bytesSent = 0;
                    Thread.Sleep(50);
                    while (bytesSent < messageBytes.Length)
                    {
                        int bytesToSend = Math.Min(chunkSize, messageBytes.Length - bytesSent);
                        byte[] chunk = Encoding.UTF8.GetBytes($"appenddata¶|~{dataId}¶|~")
                            .Concat(messageBytes.Skip(bytesSent).Take(bytesToSend))
                            .ToArray();

                        await _stream.WriteAsync(chunk, 0, chunk.Length);
                        bytesSent += bytesToSend;
                    }
                    Thread.Sleep(50);
                    string endMessage = $"enddata¶|~{dataId}";
                    byte[] endMessageBytes = Encoding.UTF8.GetBytes(endMessage);
                    await _stream.WriteAsync(endMessageBytes, 0, endMessageBytes.Length);

                    return "Message sent successfully.";
                }
                else
                {
                    byte[] fullMessage = Encoding.UTF8.GetBytes($"singlemessage¶|~{packetId ?? "nullid"}¶|~{message}");
                    await _stream.WriteAsync(fullMessage, 0, fullMessage.Length);
                    return "Message sent successfully.";
                }
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private async void StartListening()
        {
            byte[] buffer = new byte[BufferSize];

            try
            {
                while (_client.Connected)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
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

                                PendingPackets[dataId] = new string[] { dataId, packetId, "" };
                            }
                            else if (messageParts[0] == "appenddata")
                            {
                                string dataId = messageParts[1];
                                if (PendingPackets.ContainsKey(dataId))
                                {
                                    PendingPackets[dataId][2] += messageParts[2];
                                }
                            }
                            else if (messageParts[0] == "enddata")
                            {
                                string dataId = messageParts[1];
                                if (PendingPackets.ContainsKey(dataId))
                                {
                                    string packetId = PendingPackets[dataId][1];
                                    string payload = PendingPackets[dataId][2];

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
                _client.Close();
            }
        }

        public async Task Listen(string? packetId = null, Func<string, string, Task> callback = null)
        {
            while (_isRunning)
            {
                foreach (KeyValuePair<string, string> packetEntry in CompletedPackets)
                {
                    string packet = packetEntry.Value;

                    bool idMatches = packetId == null || packetEntry.Key == packetId;

                    if (idMatches)
                    {
                        if (callback != null)
                        {
                            await callback.Invoke(packetEntry.Key, packet);
                        }
                        CompletedPackets.Remove(packetEntry.Key);
                        break;
                    }
                }
                await Task.Delay(100);
            }
        }
    }
}
