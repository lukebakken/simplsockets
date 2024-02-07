using SimplPipelines;
using SimplSockets;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DemoClient
{
    internal class Program
    {
        private const ushort port = 55000;

        private static Task Main(string[] args)
        {
            return RunViaPipelines();
            /*
            string option;
TryAgain:
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("1: run client via SimplPipelines");
                Console.WriteLine("2: run client via SimplSockets");
                Console.WriteLine("3: run benchmark via SimplPipelines");
                Console.WriteLine("4: run benchmark via SimplSockets");
                option = Console.ReadLine();
            }
            else
            {
                option = args[0];
                args = null;
            }
            switch (option)
            {
                case "1": return RunViaPipelines();
                case "2": return RunViaSockets();
                case "3": return RunBenchmarkViaPipelines();
                case "4": return RunBenchmarkViaSockets();
                default: goto TryAgain;

            }
            */
        }

        private static async Task RunBenchmarkViaSockets()
        {
            using (var client = new SimplSocketClient(() => new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)))
            {
                client.Connect(new IPEndPoint(IPAddress.Loopback, port));
                var work = new SocketsWorkUnit(client);
                await BenchmarkClient(work);
            }
        }

        private static async Task RunBenchmarkViaPipelines()
        {
            using (var client = await SimplPipelineClient.ConnectAsync(
    new IPEndPoint(IPAddress.Loopback, port)))
            {
                var work = new PipelinesWorkUnit(client);
                await BenchmarkClient(work);
            }
        }

        private static readonly byte[] gibberish = new byte[512];

        private static async ValueTask BenchmarkClient(WorkUnit workUnit, int iterations = 10, int countPerIteration = 100)
        {
            for (int i = 0; i < 10; i++)
            {
                var watch = Stopwatch.StartNew();
                await workUnit.Execute(gibberish, countPerIteration);
                watch.Stop();
                Console.WriteLine($"{countPerIteration}x{gibberish.Length}: {watch.ElapsedMilliseconds}ms");
            }
        }

        private abstract class WorkUnit
        {
            public abstract ValueTask Execute(byte[] payload, int count);
        }

        private class SocketsWorkUnit : WorkUnit
        {
            public SimplSocketClient Client { get; }
            public SocketsWorkUnit(SimplSocketClient client)
                => Client = client;
            public override ValueTask Execute(byte[] payload, int count)
            {
                for (int i = 0; i < count; i++)
                    GC.KeepAlive(Client.SendReceive(payload));
                return default;
            }
        }

        private class PipelinesWorkUnit : WorkUnit
        {
            public SimplPipelineClient Client { get; }
            public PipelinesWorkUnit(SimplPipelineClient client)
                => Client = client;
            public override async ValueTask Execute(byte[] payload, int count)
            {
                for (int i = 0; i < count; i++)
                    GC.KeepAlive(await Client.SendReceiveAsync(payload));
            }
        }

        private static async Task RunViaPipelines()
        {
            using (var client = await SimplPipelineClient.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, port)))
            {
                await Console.Out.WriteLineAsync(
                    "Client connected; type 'q' to quit, anything else to send");
                await Console.Out.WriteLineAsync(
                    "(note the console can get jammed; this doesn't mean broadcasts aren't arriving)");

                // subscribe to broadcasts
                client.MessageReceived += async msg => { if (!msg.Memory.IsEmpty) await WriteLineAsync('*', msg); };

                string line;
                while ((line = await Console.In.ReadLineAsync()) != null)
                {
                    if (line == "q") break;

                    IMemoryOwner<byte> response;
                    using (var leased = line.Encode())
                    {
                        response = await client.SendReceiveAsync(leased.Memory);
                    }
                    await WriteLineAsync('<', response);
                }
            }
        }

        private static async Task RunViaSockets()
        {
            using (var client = new SimplSocketClient(() => new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            { NoDelay = true }))
            {
                client.Connect(new IPEndPoint(IPAddress.Loopback, port));
                await Console.Out.WriteLineAsync(
                    "Client connected; type 'q' to quit, anything else to send");
                await Console.Out.WriteLineAsync(
                    "(note the console can get jammed; this doesn't mean broadcasts aren't arriving)");
                // subscribe to broadcasts
                client.MessageReceived += async (s, e) => await WriteLineAsync('*', e.ReceivedMessage.Message);

                string line;
                while ((line = await Console.In.ReadLineAsync()) != null)
                {
                    if (line == "q") break;

                    var request = Encoding.UTF8.GetBytes(line);
                    var response = client.SendReceive(request);
                    await WriteLineAsync('<', response);
                }
            }
        }

        private static async ValueTask WriteLineAsync(char prefix, IMemoryOwner<byte> encoded)
        {
            using (encoded)
            {
                await WriteLineAsync(prefix, encoded.Memory);
            }
        }

        private static async ValueTask WriteLineAsync(char prefix, ReadOnlyMemory<byte> encoded)
        {
            using (var leased = encoded.Decode(encoding: Encoding.UTF8))
            {
                await Console.Out.WriteAsync(prefix);
                await Console.Out.WriteAsync(' ');
#if NET
                await Console.Out.WriteLineAsync(leased.Memory);
#else
                await Console.Out.WriteLineAsync(leased.Memory.ToArray());
#endif
            }
        }
    }
}
