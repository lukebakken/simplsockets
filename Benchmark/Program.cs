using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using DemoServer;
using SimplPipelines;
using SimplSockets;

namespace Benchmark
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<Benchmarks>();
            Console.WriteLine(summary);
        }
    }

    // note: MemoryDiagnoser here won't work well on CoreJob, due to
    // the GC not making total memory usage available
    [SimpleJob(runtimeMoniker: RuntimeMoniker.Net60), SimpleJob(runtimeMoniker: RuntimeMoniker.Net472), MemoryDiagnoser, WarmupCount(2), IterationCount(10)]
    public class Benchmarks
    {
        private static readonly EndPoint
            s1 = new IPEndPoint(IPAddress.Loopback, 6000),
            s2 = new IPEndPoint(IPAddress.Loopback, 6001);
        private byte[] _data;
        private IDisposable _socketServer, _pipeServer;
        [GlobalSetup]
        public void Setup()
        {
            var socketServer = new SimplSocketServer(CreateSocket);
            socketServer.Listen(s1);
            _socketServer = socketServer;
            var pipeServer = SimplPipelineSocketServer.For<ReverseServer>();
            pipeServer.Listen(s2);
            _pipeServer = pipeServer;

            _data = new byte[1024];
        }

        private static readonly Func<Socket> CreateSocket = () => new Socket(
            AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        { NoDelay = true };

        private static void Dispose<T>(ref T field) where T : class, IDisposable
        {
            if (field != null) try { field.Dispose(); } catch { }
        }
        [GlobalCleanup]
        public void TearDown()
        {
            Benchmarks.Dispose(ref _socketServer);
            Benchmarks.Dispose(ref _pipeServer);

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();
            CheckForLeaks();
        }

        private static void CheckForLeaks()
        {
            int leaks = MemoryOwner.LeakCount<byte>();
            if (leaks != 0) throw new InvalidOperationException($"Failed to dispose {leaks} byte-leases");
        }

        private const int Ops = 1000;

        private long AssertResult(long result)
        {
            int expected = _data.Length * Ops;
            if (result != expected) throw new InvalidOperationException(
                $"Data error: expected {expected}, got {result}");
            CheckForLeaks();
            return result;
        }

        //[Benchmark(OperationsPerInvoke = Ops)]
        public long c1_s1()
        {
            long x = 0;
            using (var client = new SimplSocketClient(CreateSocket))
            {
                client.Connect(s1);
                for (int i = 0; i < Ops; i++)
                {
                    var response = client.SendReceive(_data);
                    x += response.Length;
                }
            }
            return AssertResult(x);
        }
        //[Benchmark(OperationsPerInvoke = Ops)]
        public long c1_s2()
        {
            long x = 0;
            using (var client = new SimplSocketClient(CreateSocket))
            {
                client.Connect(s2);
                for (int i = 0; i < Ops; i++)
                {
                    var response = client.SendReceive(_data);
                    x += response.Length;
                }
            }
            return AssertResult(x);
        }
        //[Benchmark(OperationsPerInvoke = Ops)]
        public async Task<long> c2_s1()
        {
            long x = 0;
            using (var client = await SimplPipelineClient.ConnectAsync(s1))
            {
                for (int i = 0; i < Ops; i++)
                {
                    using (var response = await client.SendReceiveAsync(_data))
                    {
                        x += response.Memory.Length;
                    }
                }
            }
            return AssertResult(x);
        }
        [Benchmark(OperationsPerInvoke = Ops)]
        public async Task<long> c2_s2()
        {
            long x = 0;
            using (var client = await SimplPipelineClient.ConnectAsync(s2))
            {
                for (int i = 0; i < Ops; i++)
                {
                    using (var response = await client.SendReceiveAsync(_data))
                    {
                        x += response.Memory.Length;
                    }
                }
            }
            return AssertResult(x);
        }
    }
}
