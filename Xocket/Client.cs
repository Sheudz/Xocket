using System.Net.Sockets;
using System.Text;

namespace Xocket
{
    public class XocketClient
    {
        public int BufferSize { get; private set; } = 1024;
        private TcpClient _client;
        private NetworkStream _stream;
        private static Dictionary<string, byte[]> PendingPackets = new Dictionary<string, byte[]>();
        private static Dictionary<string, byte[]> CompletedPackets = new Dictionary<string, byte[]>();
        private bool _isRunning = true;
        private List<CancellationTokenSource> _listenerCancellationTokens = new List<CancellationTokenSource>();

        public Result Connect(string host, int port)
        {
            try
            {
                _client = new TcpClient();
                _client.Connect(host, port);
                _stream = _client.GetStream();
                StartListening();
                return Result.Ok("Connection established successfully.");
            }
            catch (Exception ex)
            {
                return Result.Fail($"Error: {ex.Message}");
            }
        }

        public Result Disconnect()
        {
            try
            {
                foreach (var tokenSource in _listenerCancellationTokens)
                {
                    tokenSource.Cancel();
                }
                _listenerCancellationTokens.Clear();

                if (_stream != null)
                    _stream.Close();

                if (_client != null)
                    _client.Close();

                return Result.Ok("Disconnected successfully.");
            }
            catch (Exception ex)
            {
                return Result.Fail($"Error: {ex.Message}");
            }
        }
        public Result SetBufferSize(int? size)
        {
            if (size < 64)
            {
                return Result.Fail("Buffer size is too small.");
            }
            else if (size > 4096)
            {
                return Result.Fail("Buffer size is too large.");
            }
            BufferSize = size ?? 1024;
            return Result.Ok();
        }

        public async Task<Result> SendMessage(string? packetId, byte[] messageBytes)
        {
            if (_client == null || !_client.Connected) return Result.Fail("Connection lost.");
            if (packetId != null && Encoding.UTF8.GetBytes(packetId).Length > BufferSize * 0.30)
            {
                return Result.Fail("Packet ID is too long.");
            }

            try
            {
                byte[] header = Encoding.UTF8.GetBytes($"singlemessage¶|~{packetId ?? "nullid"}¶|~");

                if (4 + messageBytes.Length + header.Length > BufferSize)
                {
                    string dataId = Guid.NewGuid().ToString();
                    string startMessage = $"{Encoding.UTF8.GetBytes($"startlistening¶|~{dataId}").Length.ToString("D4")}startlistening¶|~{dataId}";

                    await _stream.WriteAsync(Encoding.UTF8.GetBytes(startMessage), 0, Encoding.UTF8.GetBytes(startMessage).Length);

                    int chunkSize = BufferSize - Encoding.UTF8.GetBytes($"appenddata¶|~{dataId}¶|~").Length - 4;
                    int bytesSent = 0;

                    while (bytesSent < messageBytes.Length)
                    {
                        int bytesToSend = Math.Min(chunkSize, messageBytes.Length - bytesSent);
                        byte[] chunkHeader = Encoding.UTF8.GetBytes($"appenddata¶|~{dataId}¶|~");
                        byte[] chunk = chunkHeader.Concat(messageBytes.Skip(bytesSent).Take(bytesToSend)).ToArray();
                        byte[] chunkLength = Encoding.UTF8.GetBytes(chunk.Length.ToString("D4"));
                        byte[] appendMessage = chunkLength.Concat(chunk).ToArray();
                        await _stream.WriteAsync(appendMessage, 0, appendMessage.Length);
                        bytesSent += bytesToSend;
                    }

                    string endMessage = Encoding.UTF8.GetBytes($"enddata¶|~{dataId}¶|~{packetId ?? "nullid"}").Length.ToString("D4") + $"enddata¶|~{dataId}¶|~{packetId ?? "nullid"}";
                    await _stream.WriteAsync(Encoding.UTF8.GetBytes(endMessage), 0, Encoding.UTF8.GetBytes(endMessage).Length);

                    return Result.Ok("Message sent successfully.");
                }
                else
                {
                    int size = 4 + header.Length + messageBytes.Length;
                    byte[] sizeHeader = Encoding.UTF8.GetBytes(size.ToString("D4"));
                    byte[] fullMessage = sizeHeader.Concat(header).Concat(messageBytes).ToArray();
                    await _stream.WriteAsync(fullMessage, 0, fullMessage.Length);
                    return Result.Ok("Message sent successfully.");
                }
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to send message: {ex.Message}");
            }
        }
        private async void StartListening()
        {
            byte[] buffer = new byte[BufferSize];

            try
            {
                while (_client.Connected)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, 4);
                    if (bytesRead == 0) break;

                    string messageSizeStr = Encoding.UTF8.GetString(buffer, 0, 4);
                    if (!int.TryParse(messageSizeStr, out int messageSize)) continue;
                    if (messageSize <= 0 || messageSize > BufferSize) continue;

                    bytesRead = await _stream.ReadAsync(buffer, 0, messageSize);
                    if (bytesRead == 0) break;

                    byte[] message = buffer.Take(bytesRead).ToArray();
                    Console.WriteLine(Encoding.UTF8.GetString(message));
                    try
                    {
                        string header = Encoding.UTF8.GetString(message);
                        string[] messageParts = header.Split(new string[] { "¶|~" }, StringSplitOptions.None);

                        if (messageParts.Length > 0)
                        {
                            if (messageParts[0] == "singlemessage")
                            {
                                string packetId = messageParts[1];
                                int headerLength = Encoding.UTF8.GetBytes($"singlemessage¶|~{packetId}¶|~").Length;
                                byte[] payload = message.Skip(headerLength).ToArray();
                                CompletedPackets[packetId] = payload;
                            }
                            else if (messageParts[0] == "startlistening")
                            {
                                string dataId = messageParts[1];

                                PendingPackets[dataId] = new byte[] { };
                            }
                            else if (messageParts[0] == "appenddata")
                            {
                                string dataId = messageParts[1];
                                int headerLength = Encoding.UTF8.GetBytes($"appenddata¶|~{dataId}¶|~").Length;
                                byte[] payload = message.Skip(headerLength).ToArray();

                                if (PendingPackets.ContainsKey(dataId))
                                {
                                    PendingPackets[dataId] = PendingPackets[dataId].Concat(payload).ToArray();
                                }
                            }
                            else if (messageParts[0] == "enddata")
                            {
                                string dataId = messageParts[1];
                                if (PendingPackets.ContainsKey(dataId))
                                {
                                    string packetId = messageParts[2];
                                    byte[] payload = PendingPackets[dataId];

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

        public Action Listen(string? packetId = null, Func<byte[], Task> callback = null)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            _listenerCancellationTokens.Add(cancellationTokenSource);

            Task.Run(async () =>
            {
                try
                {
                    while (_isRunning && !cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        foreach (KeyValuePair<string, byte[]> packetEntry in CompletedPackets)
                        {
                            byte[] packet = packetEntry.Value;

                            bool idMatches = packetId == null || packetEntry.Key == packetId;

                            if (idMatches)
                            {
                                if (callback != null)
                                {
                                    await callback.Invoke(packet);
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

            return () => cancellationTokenSource.Cancel();
        }

        public void StopListening(Action stopListenAction)
        {
            stopListenAction();
        }
    }
}