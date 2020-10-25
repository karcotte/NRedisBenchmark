using System;
using StackExchange.Redis;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using CommandLine;
using Newtonsoft.Json;

namespace NRedisBenchmark
{
    class Program
    {
        class Options
        {
            [Option("app_threads", Required = false, HelpText = "Set the number of application threads.", Default = 24)]
            public int AppThreads { get; set; }

            [Option('d', "dynamic", Required = false, HelpText = "Application threads are created with 1 second interval.", Default = true)]
            public bool Dynamic { get; set; }

            [Option("worker_threads", Required = false, HelpText = "Set the number of .net worker threads.", Default = 24)]
            public int WorkerThreads { get; set; }

            [Option("io_threads", Required = false, HelpText = "Set the number of .net io threads.", Default = 24)]
            public int IOThreads { get; set; }

            [Option('p', "profile", Required = false, HelpText = "When set to true, the application will wait 30 seconds for attaching profilers.", Default = false)]
            public bool Profile { get; set; }

            [Option("hash_size", Required = false, HelpText = "Set the number of fields in a hash.", Default = 15)]
            public int HashSize { get; set; }

            [Option('k', "key_name", Required = false, HelpText = "Set the hash key name.", Default = "key")]
            public string KeyName { get; set; }

            [Option('m', "mux_count", Required = false, HelpText = "Set the number of multiplexers.", Default = 1)]
            public int MuxCount { get; set; }

            [Option('t', "time_out", Required = false, HelpText = "Set the timeout (in minutes).", Default = 10)]
            public int TimeOut { get; set; }

            [Option('r', "requests", Required = false, HelpText = "Number of requests to execute for each application thread.", Default = 100000)]
            public int Requests { get; set; }
        }

        private static int _hashSize;
        private static int _workerThreads;
        private static int _ioThreads;
        private static int _appThreads;
        private static bool _dynamic;
        private static string _keyName;
        private static bool _profile;
        private static int _muxCount;
        private static TimeSpan _timeout;
        private static int _requests;
        private static CancellationTokenSource _ct;
        private static readonly Random _rand = new Random();

        private static long _counter = 0;
        private static long _latency = 0;

        private static IConnectionMultiplexer[] _multiplexers;

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                   .WithParsed<Options>(o =>
                   {
                       Console.WriteLine(JsonConvert.SerializeObject(o, Formatting.Indented));
                       _profile = o.Profile;
                       _keyName = o.KeyName;
                       _hashSize = o.HashSize;
                       _workerThreads = o.WorkerThreads;
                       _ioThreads = o.IOThreads;
                       _appThreads = o.AppThreads;
                       _dynamic = o.Dynamic;
                       _muxCount = o.MuxCount;
                       _multiplexers = new IConnectionMultiplexer[_muxCount];
                       _timeout = TimeSpan.FromMinutes(o.TimeOut);
                       _requests = o.Requests;
                       execute();
                   });
        }

        private static void execute()
        {
            if (_profile)
            {
                Console.WriteLine($"Waiting 30 seconds for you to run attach with monitor tools. PID is {Process.GetCurrentProcess().Id}");
                Thread.Sleep(30000);
            }
            ThreadPool.SetMinThreads(_workerThreads, _ioThreads);
            ThreadPool.GetMinThreads(out _workerThreads, out _ioThreads);
            _ct = new CancellationTokenSource(_timeout);
            for (int i = 0; i < _muxCount; i++)
            {
                _multiplexers[i] = ConnectionMultiplexer.Connect("localhost");
            }
           _multiplexers[0].GetDatabase().KeyDelete(_keyName);
            var t = Task.Factory.StartNew(() => printResults());
            for (int i = 0; i < _appThreads; i++)
            {
                ThreadPool.QueueUserWorkItem(state => RedisQuery(_multiplexers[i % _muxCount]));
                Console.WriteLine($"Created {i + 1} application threads");
                Console.WriteLine($"number of fields in hash {_hashSize}, multiplexers {_muxCount}, application threads {i}, .net worker threads {_workerThreads}, .net io threads {_ioThreads}");
                if (_dynamic)
                {
                    Thread.Sleep(1000);
                }
            }
            t.Wait();
        }

        public static void RedisQuery(object obj)
        {
            int requestsSent = 0;
            while (!_ct.IsCancellationRequested && requestsSent < _requests)
            {
                Thread.Sleep(_rand.Next(1, 20));
                var multiplexer = (IConnectionMultiplexer)obj;
                var db = multiplexer.GetDatabase();
                var start = DateTime.Now;
                db.HashGetAll(_keyName);
                var entries = new HashEntry[_hashSize];
                for (var i = 0; i < _hashSize; i++)
                {
                    entries[i] = new HashEntry(i, i);
                }
                db.HashSet(_keyName, entries);
                Interlocked.Increment(ref _counter);
                Interlocked.Add(ref _latency, (DateTime.Now - start).Ticks);
                requestsSent++;
            }
        }

        public static void printResults()
        {
            while (!_ct.IsCancellationRequested)
            {
                Thread.Sleep(1000);
                for (int i = 0; i < _muxCount; i++)
                {
                    Console.WriteLine(_multiplexers[i].GetStatus());
                }
                Console.WriteLine($"number of fields in hash {_hashSize}, multiplexers {_muxCount}, application threads {_appThreads}, .net worker threads {_workerThreads}, .net io threads {_ioThreads}");
                Console.WriteLine($"Avg requests {Interlocked.Read(ref _counter)}");
                Console.WriteLine($"Avg latency {0.001 * (double)Interlocked.Read(ref _latency) / Interlocked.Read(ref _counter)}ms");
                Interlocked.Exchange(ref _counter, 0);
                Interlocked.Exchange(ref _latency, 0);
            }
        }
    }
}
