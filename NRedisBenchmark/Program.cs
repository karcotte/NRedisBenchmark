using System;
using StackExchange.Redis;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using CommandLine;
using Newtonsoft.Json;
using ServiceStack.Redis;
using ServiceStack;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ServiceStack.DataAnnotations;

namespace NRedisBenchmark
{
    class Program
    {
        class Options
        {
            [Option("app_threads", Required = false, HelpText = "Set the number of application threads.", Default = 48)]
            public int AppThreads { get; set; }

            [Option('d', "dynamic", Required = false, HelpText = "Application threads are created with 1 second interval.", Default = true)]
            public bool Dynamic { get; set; }

            [Option("service_stack", Required = false, HelpText = "Uses the ServiceStack.Redis library instead of StackExchange.Redis library.", Default = false)]
            public bool ServiceStack { get; set; }

            [Option("async", Required = false, HelpText = "Uses the StackExchange.Redis asynchronous interface (but still with a blocked wait for the result).", Default = false)]
            public bool Async { get; set; }

            [Option("pooled_connections", Required = false, HelpText = "Set the number of pooled connections enabled for ServiceStack.Redis.", Default = 1)]
            public int PooledConnections { get; set; }

            [Option("worker_threads", Required = false, HelpText = "Set the number of .net worker threads.", Default = 64)]
            public int WorkerThreads { get; set; }

            [Option("io_threads", Required = false, HelpText = "Set the number of .net io threads.", Default = 64)]
            public int IOThreads { get; set; }

            [Option('p', "profile", Required = false, HelpText = "When set to true, the application will wait 30 seconds for attaching profilers.", Default = false)]
            public bool Profile { get; set; }

            [Option("hash_size", Required = false, HelpText = "Set the number of fields in a hash.", Default = 15)]
            public int HashSize { get; set; }

            [Option("payload_size", Required = false, HelpText = "Set the size of each field in a hash.", Default = 128)]
            public int PayloadSize { get; set; }


            [Option('k', "key_name", Required = false, HelpText = "Set the hash key name.", Default = "key")]
            public string KeyName { get; set; }

            [Option('m', "mux_count", Required = false, HelpText = "Set the number of multiplexers.", Default = 1)]
            public int MuxCount { get; set; }

            [Option('t', "time_out", Required = false, HelpText = "Set the timeout (in minutes).", Default = 1)]
            public int TimeOut { get; set; }

            [Option('r', "requests", Required = false, HelpText = "Number of requests to execute for each application thread.", Default = 10000)]
            public int Requests { get; set; }
        }

        private static int _hashSize;
        private static int _payloadSize;
        private static int _workerThreads;
        private static int _ioThreads;
        private static int _appThreads;
        private static bool _dynamic;
        private static bool _serviceStack;
        private static bool _async;
        private static int _pooledConnections;
        private static string _keyName;
        private static bool _profile;
        private static int _muxCount;
        private static TimeSpan _timeout;
        private static int _requests;
        private static CancellationTokenSource _ct;
        private static readonly Random _rand = new Random();

        private static long _counter = 0;
        private static long _latency = 0;

        private static ConnectionMultiplexer[] _multiplexers;
        private static PooledRedisClientManager[] _managers;

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                   .WithParsed<Options>(o =>
                   {
                       Console.WriteLine(JsonConvert.SerializeObject(o, Formatting.Indented));
                       _profile = o.Profile;
                       _keyName = o.KeyName;
                       _hashSize = o.HashSize;
                       _payloadSize = o.PayloadSize;
                       _workerThreads = o.WorkerThreads;
                       _ioThreads = o.IOThreads;
                       _appThreads = o.AppThreads;
                       _dynamic = o.Dynamic;
                       _serviceStack = o.ServiceStack;
                       _async = o.Async;
                       _pooledConnections = o.PooledConnections;
                       _muxCount = o.MuxCount;
                       _multiplexers = new ConnectionMultiplexer[_muxCount];
                       _managers = new PooledRedisClientManager[_muxCount];
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
            //ConnectionMultiplexer.SetFeatureFlag("preventthreadtheft", true);
            ThreadPool.GetMinThreads(out _workerThreads, out _ioThreads);
            _ct = new CancellationTokenSource(_timeout);
            for (int i = 0; i < _muxCount; i++)
            {
                _multiplexers[i] = ConnectionMultiplexer.Connect("redis-14151.c238.us-central1-2.gce.cloud.redislabs.com:14151,syncTimeout=5000,password=FRUP9fos5vop_zush");
                //_multiplexers[i] = ConnectionMultiplexer.Connect("localhost:12000,syncTimeout=5000");
                _multiplexers[i].IncludeDetailInExceptions = true;
                //RedisClientManagerConfig config = new RedisClientManagerConfig();
                
                _managers[i] = new PooledRedisClientManager(_pooledConnections, 5, "FRUP9fos5vop_zush@redis-14151.c238.us-central1-2.gce.cloud.redislabs.com:14151");                
                //_managers[i] = new PooledRedisClientManager(10, 5, "localhost:12000");
                _managers[i].ConnectTimeout = 5000;                
            }
           _multiplexers[0].GetDatabase().KeyDelete(_keyName);
            var t = Task.Factory.StartNew(() => printResults());
            for (int i = 0; i < _appThreads; i++)
            {
                if(!_serviceStack)
                {
                    if(!_async)
                    {
                        ThreadPool.QueueUserWorkItem(state => RedisQuery(_multiplexers[i % _muxCount]));
                    } else
                    {
                        ThreadPool.QueueUserWorkItem(state => RedisQueryBlockedAsync(_multiplexers[i % _muxCount]));
                    }
                    
                } else
                {
                    ThreadPool.QueueUserWorkItem(state => RedisPooledQuery(_managers[i % _muxCount]));
                }                                
                Console.WriteLine($"Created {i + 1} application threads");
                Console.WriteLine($"number of fields in hash {_hashSize}, multiplexers {_muxCount}, application threads {i}, .net worker threads {_workerThreads}, .net io threads {_ioThreads}");
                if (_dynamic)
                {
                    Thread.Sleep(100);
                }
            }
            t.Wait();
        }

        public static void RedisQuery(object obj)
        {
            int requestsSent = 0;
            while (!_ct.IsCancellationRequested && requestsSent < _requests)
            {
                try
                {
                    Thread.Sleep(_rand.Next(1, 20));
                    var multiplexer = (IConnectionMultiplexer)obj;
                    var db = multiplexer.GetDatabase();
                    var payload = new byte[_payloadSize];
                    _rand.NextBytes(payload);
                    var start = DateTime.Now;
                    db.HashGetAll(_keyName);
                    var entries = new HashEntry[_hashSize];
                    for (var i = 0; i < _hashSize; i++)
                    {
                        entries[i] = new HashEntry(i, payload);
                    }
                    db.HashSet(_keyName, entries);
                    Interlocked.Increment(ref _counter);
                    Interlocked.Add(ref _latency, (DateTime.Now - start).Ticks);
                    requestsSent++;
                }
                catch(Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        public static void RedisQueryBlockedAsync(object obj)
        {
            int requestsSent = 0;
            while (!_ct.IsCancellationRequested && requestsSent < _requests)
            {
                try
                {
                    Thread.Sleep(_rand.Next(1, 20));
                    var multiplexer = (IConnectionMultiplexer)obj;
                    var db = multiplexer.GetDatabase();
                    var payload = new byte[_payloadSize];
                    _rand.NextBytes(payload);
                    var start = DateTime.Now;
                    var getAllTask = db.HashGetAllAsync(_keyName);
                    db.Wait(getAllTask);
                    var entries = new HashEntry[_hashSize];
                    for (var i = 0; i < _hashSize; i++)
                    {
                        entries[i] = new HashEntry(i, payload);
                    }
                    var hashSetTask = db.HashSetAsync(_keyName, entries);
                    db.Wait(hashSetTask);
                    Interlocked.Increment(ref _counter);
                    Interlocked.Add(ref _latency, (DateTime.Now - start).Ticks);
                    requestsSent++;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        public static void RedisPooledQuery(object obj)
        {
            int requestsSent = 0;
            while (!_ct.IsCancellationRequested && requestsSent < _requests)
            {
                try
                {
                    Thread.Sleep(_rand.Next(1, 20));
                    var manager = (PooledRedisClientManager)obj;
                    RedisSinglePooledQuery(manager);
                    requestsSent++;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }            
        }

        public static void RedisSinglePooledQuery(PooledRedisClientManager manager) {
            var payload = new byte[_payloadSize];
            _rand.NextBytes(payload);
            var start = DateTime.Now;
            var redis = manager.GetClient();
            using (redis)
            {                
                redis.GetHashValues(_keyName);
                var entries = new List<KeyValuePair<string, string>>(_hashSize);
                for (var i = 0; i < _hashSize; i++)
                {
                    entries.Add(new KeyValuePair<string, string>("" + i, payload.ToString()));
                }
                redis.SetRangeInHash(_keyName, entries);
            }
            
            Interlocked.Increment(ref _counter);
            Interlocked.Add(ref _latency, (DateTime.Now - start).Ticks);
        }

        public static void printResults()
        {
            while (!_ct.IsCancellationRequested)
            {
                Thread.Sleep(1000);
                if(!_serviceStack)
                {
                    for (int i = 0; i < _muxCount; i++)
                    {
                        Console.WriteLine(_multiplexers[i].GetStatus());
                    }
                } else
                {
                    for (int i = 0; i < _muxCount; i++)
                    {
                        var stats = _managers[i].GetStats();
                        foreach (KeyValuePair<string, string> kvp in stats)
                        {
                            Console.WriteLine("Key = {0}, Value = {1}", kvp.Key, kvp.Value);
                        }
                    }
                }                
                
                int _availableWorkerThreads;
                int _availableIoThreads;
                int _busyWorkerThreads;
                int _busyIoThreads;
                int _maxWorkerThreads;
                int _maxIoThreads;
                ThreadPool.GetAvailableThreads(out _availableWorkerThreads, out _availableIoThreads);
                ThreadPool.GetMaxThreads(out _maxWorkerThreads, out _maxIoThreads);
                _busyWorkerThreads =  _maxWorkerThreads - _availableWorkerThreads;
                _busyIoThreads = _maxIoThreads - _availableIoThreads;
                var counter = Interlocked.Read(ref _counter);
                var latency = Interlocked.Read(ref _latency);
                Console.WriteLine($"number of fields in hash {_hashSize}, multiplexers {_muxCount}, application threads {_appThreads}, .net min worker threads {_workerThreads}, .net min io threads {_ioThreads}, .net busy worker threads {_busyWorkerThreads}, .net busy worker threads {_busyIoThreads}");
                Console.WriteLine($"Avg requests {counter}");
                Console.WriteLine($"Avg latency {0.001 * (double) latency / counter}ms");
                Interlocked.Exchange(ref _counter, 0);
                Interlocked.Exchange(ref _latency, 0);
            }
        }
    }
}
