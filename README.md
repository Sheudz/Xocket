# Xocket

### Xocket is a lightweight and efficient C# library for TCP-based socket communication, designed to filter and forward packets of any length. Built for flexibility and performance, Xocket simplifies working with TCP sockets while providing fine-grained control over packet handling.

## Features
- ### Packet Filtering: Easily inspect and filter incoming and outgoing packets.
- ### Message Forwarding: Supports forwarding messages of any length between clients and servers.
- ### Customizable Logic: Allows developers to define custom packet handling logic.
- ### High Performance: Optimized for reliable and low-latency communication.
- ### Flexible API: Simplifies working with TCP sockets, reducing boilerplate code.

## Installation
### not available now

# Examples

### Xocket Server:
```c#
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xocket;

namespace XocketTestServer
{
    class Program
    {
        private static XocketServer server = new XocketServer();

        static async Task Main(string[] args)
        {
            server.StartServer(5555);
            Task FuncOneTask = FuncOne();
            Task FuncTwoTask = FuncTwo();
            await Task.WhenAll(FuncOneTask, FuncTwoTask);
        }
        private static async Task FuncOne()
        {
            Console.WriteLine("Thread 1 started!");
            await server.Listen("funcone", callback: async (client, message) =>
            {
                Console.WriteLine($"func1: {message}");
            });
        }
        private static async Task FuncTwo()
        {
            Console.WriteLine("Thread 2 started!");
            await server.Listen("functwo", callback: async (client, message) =>
            {
                Console.WriteLine($"func2: {message}");
            });
        }
    }
}

```
### Xocket Client:
```c#
using System;
using System.Linq;
using System.Threading.Tasks;
using Xocket;

namespace XocketTestClient
{
    class Program
    {
        private static XocketClient client = new XocketClient();

        static async Task Main(string[] args)
        {
            Result res = client.Connect("127.0.0.1", 5555);
            Console.WriteLine(res.Message);
            Task FuncOneTask = FuncOne();
            Task FuncTwoTask = FuncTwo();
            await Task.WhenAll(FuncOneTask, FuncTwoTask);
        }

        private static async Task FuncOne()
        {
            Console.WriteLine("Thread 1 started!");
            while (true)
            {
                Result messageresult = await client.SendMessage("funcone", "test1");
                Console.WriteLine("func1: " + messageresult.Message);
                await Task.Delay(100);
            }
        }

        private static async Task FuncTwo()
        {
            Console.WriteLine("Thread 2 started!");
            while (true)
            {
                Result messageresult = await client.SendMessage("functwo", "test2");
                Console.WriteLine("func2: " + messageresult.Message);
                await Task.Delay(100);
            }
        }

    }
}
```

# Creating an Instance of XocketServer or XocketClient
The Xocket library provides two primary classes: XocketServer for hosting a server and XocketClient for connecting as a client. Below are step-by-step guides for creating instances of each class.
### Creating a XocketServer Instance
The XocketServer class is used to create a TCP server that listens for client connections and processes incoming packets.
```c#
using Xocket;
class Program
{
    public static void Main()
    {
        XocketServer server = new XocketServer();
    }
}
```
### Creating a XocketClient Instance
The XocketClient class is used to create a TCP client that connects to a server and exchanges messages.
```c#
using Xocket;
class Program
{
    public static void Main()
    {
        XocketClient client = new XocketClient();
    }
}
```
# Methods Overview

## StartServer(int port) (SERVER ONLY)
### Starts the TCP server on a specified port.
#### Parameters:
- `port` (int): The port number to bind the server to. Must be between 0 and 65535.
#### Returns:
- Result class
    - Success: `Server started successfully.`
    - Failure: `Invalid port.", "Server is already running.", or any exception message.`
#### Example:
```c#
Result result = server.StartServer(8080);
Console.WriteLine(result.Success, result.Message);
```

## StopServer() (SERVER ONLY)
### Starts the TCP server on a specified port.
#### Parameters: `None.`
#### Returns:
- Result class
    - Success: `Server stopped successfully.`
    - Failure: `Server is not running." or any exception message.`
#### Example:
```c#
Result result = server.StopServer();
Console.WriteLine(result.Success, result.Message);
```

## Connect(string host, int port) (CLIENT ONLY)
### Establishes a connection to a remote TCP server.
#### Parameters:
- `host` (string): The hostname or IP address of the server.
- `port` (int): The port number of the server. Must be between `0` and `65535`.
#### Returns:
- Result class
    - Success: `Connection established successfully.`
    - Failure: `Error: {exception message}`
#### Example:
```c#
Result result = client.Connect("127.0.0.1", 8080);
if (result.Success)
{
    Console.WriteLine("Connected to the server.");
}
else
{
    Console.WriteLine($"Failed to connect: {result.Message}");
}

```

## Disconnect() (CLIENT ONLY)
### Terminates the connection to the remote TCP server.
#### Parameters: `None.`
#### Returns:
- Result class
    - Success: `Disconnected successfully.`
    - Failure: `Error: {exception message}`
#### Example:
```c#
Result result = client.Connect("127.0.0.1", 8080);
if (result.Success)
{
    Console.WriteLine("Connected to the server.");
}
else
{
    Console.WriteLine($"Failed to connect: {result.Message}");
}
```

## SetBufferSize(int? size)
### Configures the buffer size for packet handling.
#### Parameters:
- `size` (nullable int): Desired buffer size in bytes. If null, defaults to 1024. Must be between 64 and 4096
#### Returns:
- Result class
    - Success: `Success.`
    - Failure: `Buffer size is too small." or "Buffer size is too large.`
#### Example:
```c#
Result result = server.StopServer();
Console.WriteLine(result.Success, result.Message);
```

## SendMessage(string? packetId, string message) (FOR CLIENT)
### Asynchronously sends a message to the connected server, optionally with a packet ID. If the message exceeds the buffer size, it is split into chunks and sent incrementally.
#### Parameters:
- `packetId` (string?): An optional identifier for the packet. If not provided, defaults to "nullid"
- `message` (string): The message content to be sent.
#### Returns:
- Result class
    - Success: `Message sent successfully.`
    - Failure: `Connection lost.`, `Packet ID is too long.`, `Failed to send message: {ex.Message}`.
#### Example:
```c#
Result result = await client.SendMessage("12345", "Hello, World!");
Console.WriteLine(result.Message);
```

## SendMessage(TcpClient client, string? packetId, string message) (FOR SERVER)
### Asynchronously sends a message to the specific client, optionally with a packet ID. If the message exceeds the buffer size, it is split into chunks and sent incrementally.
#### Parameters:
- `client` (TcpClient): client to whom the message will be sent
- `packetId` (string?): An optional identifier for the packet. If not provided, defaults to "nullid"
- `message` (string): The message content to be sent.
#### Returns:
- Result class
    - Success: `Message sent successfully.`
    - Failure: `Connection lost.`, `Packet ID is too long.`, `Failed to send message: {ex.Message}`.
#### Example:
```c#
Result result = await server.SendMessage(client, "12345", "Hello, World!");
Console.WriteLine(result.Message);
```

# Event Handler Registrator
## OnDisconnect() (SERVER ONLY)
### The OnDisconnect method acts as an event handler registrar. This means it allows associating a specific piece of logic (a handler) with an event — in this case, the event of a client disconnecting.
#### Parameters:
- `client` (TcpClient): client to whom the message will be sent
- `callback` (ActionCallck): The Action delegate represents a method that does not return a value and is called when the client disconnects.
#### Returns: `None.`
#### Example:
```c#
server.OnDisconnect(client, () =>
{
    Console.WriteLine($"Client {client.Client.RemoteEndPoint} has disconnected.");
});
```

## Listen(string? packetId = null, TcpClient? specificClient = null, Func<TcpClient, string, Task> callback = null) (FOR SERVER)
### This asynchronous method listens for incoming messages and triggers a provided callback when specific packets are completed, based on a packet ID, a specific client, or both.
#### Parameters:
- `packetId` (string?) This is an optional parameter used to filter packets by their unique identifier. If you want to listen for messages from a specific packet, you can provide its packetId.
- `specificClient` (TcpClient?) This is an optional parameter that allows you to filter packets by a specific client. If you want to only process packets from a certain client, you can provide its TcpClient instance.
- `callback` (Func<TcpClient, string, Task>) This is an optional asynchronous callback function that is invoked when a matching packet is found.
#### Callback receives:
- `TcpClient`: The client (TcpClient) that sent the packet.
- `string`: The packet's content (message or data).
#### Examples:
```c#
await server.Listen("packetid123", callback: async (client, packet) =>
{
    Console.WriteLine($"Received packet {packet} from client {client.Client.RemoteEndPoint}");
});
```

## Listen(string? packetId = null, Func<string, string, Task> callback = null) (FOR CLIENT)
### This asynchronous method listens for completed packets and invokes the provided callback when a matching packet is found, based on the optional packetId filter.
#### Parameters:
- `packetId` (string?) This is an optional parameter used to filter packets by their unique identifier. If you want to listen for messages from a specific packet, you can provide its packetId.
- `callback` (Func<string, Task>) This is an optional asynchronous callback function that is invoked when a matching packet is found.
#### Callback receives:
- `string`: The packet's content (message or data).
#### Examples:
```c#
await client.Listen("packet123", async (packet) =>
{
    Console.WriteLine($"Received packet: {packet}");
});
```

# License
Xocket is licensed under the MIT License. See the LICENSE file for details.

### Happy coding with Xocket! 🚀
