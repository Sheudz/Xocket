using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Xocket
{
    public class XocketServer
    {
        private TcpListener _listener;
        private bool _isRunning;
        public int buffersize = 1024;
        static Dictionary<string, string[]> pendingPackets = new Dictionary<string, string[]>();
        static Dictionary<string, Tuple<string, TcpClient>> completedPackets = new Dictionary<string, Tuple<string, TcpClient>>();

        public string StartServer(int port)
        {
            if (port < 0 || port > 65365)
            {
                return "Incorrect port.";
            }
            if (_isRunning) return "Server already is running.";

            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _isRunning = true;

            ListenForConnectionsAsync();
            return "successful";
        }

        public string StopServer()
        {
            if (!_isRunning) return "Server doesnt run.";

            _isRunning = false;
            _listener.Stop();
            return "successful";
        }

        public async Task Listen(string? packetid = null, TcpClient? specificClient = null, Func<TcpClient, string, Task> callback = null)
        {
            while (_isRunning)
            {
                foreach (var packetEntry in completedPackets)
                {
                    var packet = packetEntry.Value;

                    bool idMatches = packetid == null || packetEntry.Key == packetid;
                    bool clientMatches = specificClient == null || packet.Item2 == specificClient;

                    if (idMatches && clientMatches)
                    {
                        await callback?.Invoke(packet.Item2, packet.Item1);
                        completedPackets.Remove(packetEntry.Key);
                        break;
                    }
                }
                await Task.Delay(100);
            }
        }
        public async Task<string> SendMessage(TcpClient client, string? packetid, string message)
        {
            if (client == null || !client.Connected) return "Connection lost.";
            if (packetid != null && Encoding.UTF8.GetBytes(packetid).Length > buffersize * 0.25)
            {
                return "Packetid too long.";
            }
            try
            {
                if (Encoding.UTF8.GetBytes(message).Length + Encoding.UTF8.GetBytes($"singlemessage¶|~{packetid}¶|~").Length > buffersize)
                {
                    NetworkStream stream = client.GetStream();
                    Random random = new Random();
                    string startmessage;
                    string dataid = $"id{random.Next(1, 999999999)}";
                    if (packetid != null) { startmessage = $"startlistening¶|~{dataid}¶|~{packetid}"; }
                    else { startmessage = $"startlistening¶|~{dataid}¶|~nullid"; }
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(startmessage), 0, Encoding.UTF8.GetBytes(startmessage).Length);
                    byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                    int chunkSize = buffersize - Encoding.UTF8.GetBytes($"appenddata¶|~{dataid}¶|~").Length;
                    int bytesSent = 0;
                    Thread.Sleep(25);
                    while (bytesSent < messageBytes.Length)
                    {
                        int bytesToSend = Math.Min(chunkSize, messageBytes.Length - bytesSent);
                        var dataToSend = new List<byte>();
                        dataToSend.AddRange(Encoding.UTF8.GetBytes($"appenddata¶|~{dataid}¶|~"));
                        dataToSend.AddRange(messageBytes.Skip(bytesSent).Take(bytesToSend));
                        await stream.WriteAsync(dataToSend.ToArray(), 0, dataToSend.Count);

                        bytesSent += bytesToSend;
                    }
                    Thread.Sleep(25);
                    await stream.WriteAsync(Encoding.UTF8.GetBytes($"enddata¶|~{dataid}"), 0, Encoding.UTF8.GetBytes($"enddata¶|~{dataid}").Length);
                    return "successful";
                }
                else
                {
                    NetworkStream stream = client.GetStream();
                    string fullmessage;
                    if (packetid != null) { fullmessage = $"singlemessage¶|~{packetid}¶|~{message}"; }
                    else { fullmessage = $"singlemessage¶|~nullid¶|~{message}"; }
                    byte[] data = Encoding.UTF8.GetBytes(fullmessage);

                    await stream.WriteAsync(data, 0, data.Length);
                    return "successful";
                }
            }
            catch (Exception ex)
            {
                return $"Error: {ex}";
            }
        }

        private async Task ListenForConnectionsAsync()
        {
            try
            {
                while (_isRunning)
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    client.NoDelay = true;
                    await HandleClientAsync(client);
                }
            }
            catch { }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            var stream = client.GetStream();
            var buffer = new byte[buffersize];

            try
            {
                while (_isRunning)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    try
                    {
                        string[] messageParts = message.Split(new string[] { "¶|~" }, StringSplitOptions.None);
                        if (messageParts.Length > 0)
                        {
                            if (messageParts.Length > 0)
                            {

                                if (messageParts[0] == "singlemessage")
                                {
                                    string command = messageParts[1];
                                    string msg = messageParts[2];
                                    completedPackets[command] = new Tuple<string, TcpClient>(msg, client);
                                }
                                else if (messageParts[0] == "startlistening")
                                {
                                    string id = messageParts[1];
                                    string command = messageParts[2];
                                    pendingPackets[id] = new string[] { id, command, "" };
                                }
                                else if (messageParts[0] == "appenddata")
                                {
                                    var packetToUpdate = pendingPackets.FirstOrDefault(p => p.Key == messageParts[1]);
                                    if (packetToUpdate.Value != null)
                                    {
                                        pendingPackets[packetToUpdate.Key][2] += messageParts[2];
                                    }
                                }
                                else if (messageParts[0] == "enddata")
                                {
                                    var packet = pendingPackets.FirstOrDefault(p => p.Key == messageParts[1]);
                                    if (packet.Value != null)
                                    {
                                        string command = packet.Value[1];
                                        string messageData = packet.Value[2];
                                        completedPackets[command] = new Tuple<string, TcpClient>(messageData, client);
                                        pendingPackets.Remove(packet.Key);
                                    }
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
            }
        }
    }
}