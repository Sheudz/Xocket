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

## Xocket Server:
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
# Xocket Client:
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
## Creating a XocketServer Instance
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
## Creating a XocketClient Instance
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
# Methods

## StartServer(int port)       (SERVER ONLY)
### Starts the TCP server on a specified port.
#### Parameters:
- `port` (int): The port number to bind the server to. Must be between 0 and 65535.
#### Returns:
- Result class
    - Success: "Server started successfully."
    - Failure: "Invalid port.", "Server is already running.", or any exception message.
#### Example:
```c#
Result result = server.StartServer(8080);
Console.WriteLine(result.Success, result.Message);
```

## StopServer()
### Starts the TCP server on a specified port.
#### Parameters: None.
#### Returns:
- Result class
    - Success: "Server stopped successfully."
    - Failure: "Server is not running." or any exception message.
#### Example:
```c#
Result result = server.StopServer();
Console.WriteLine(result.Success, result.Message);
```    

# License
Xocket is licensed under the MIT License. See the LICENSE file for details.

### Happy coding with Xocket! 🚀
