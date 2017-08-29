using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace PacketLossTester
{
    public static class Extensions
    {
        public static double StdDev<T>(this IEnumerable<T> list, Func<T, double> values)
        {
            var mean = 0.0;
            var sum = 0.0;
            var stdDev = 0.0;
            var n = 0;
            foreach (var value in list.Select(values))
            {
                n++;
                var delta = value - mean;
                mean += delta / n;
                sum += delta * (value - mean);
            }
            if (1 < n)
                stdDev = Math.Sqrt(sum / (n - 1));

            return stdDev;

        }
    }

    public class PacketLossTest
    {
        public class PingReplies
        {
            public List<PingReply> Responses { get; set; }

            public double GetJitter(int packetCount = 50)
            {
                if (packetCount == -1)
                {
                    return Responses.StdDev(rp => Convert.ToDouble(rp.RoundtripTime));
                }
                else
                {
                    return Responses.Take(50).StdDev(rp => Convert.ToDouble(rp.RoundtripTime));
                }
            }

            public double GetAverageResponseTime()
            {
                return Responses.Count == 0 ? 0 : Responses.Average(r => r.RoundtripTime);
            }

            public int GetSuccessfulResponseCount()
            {
                return Responses.Count(r => r.Status == IPStatus.Success);
            }

            public int GetFailedResponseCount()
            {
                return Responses.Count(r => r.Status != IPStatus.Success);
            }

            public PingReplies()
            {
                Responses = new List<PingReply>();
            }
        }

        public ConcurrentDictionary<string, PingReplies> Responses;
        private IEnumerable<string> _hosts;
        private int _interval;
        private Timer _timer;

        public int GetInterval()
        {
            return _interval;
        }

        public PacketLossTest(IEnumerable<string> hosts, int interval)
        {
            _hosts = hosts;
            _interval = interval;

            Responses = new ConcurrentDictionary<string, PingReplies>();

            foreach (var host in hosts)
            {
                Responses.TryAdd(host, new PacketLossTester.PacketLossTest.PingReplies());
            }
        }

        private PingReply Ping(string host)
        {
            PingReply pingReply = null;

            try
            {
                pingReply = new Ping().Send(host);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception thrown while attempting ping.");
                Console.ReadLine();
                Environment.Exit(1);
            }

            return pingReply;
        }

        private void HandleTimer(Object source, ElapsedEventArgs e)
        {
            Parallel.ForEach(_hosts, h => Responses[h].Responses.Add(Ping(h)));
        }

        public void Start()
        {
            _timer = new Timer(_interval);
            _timer.Elapsed += HandleTimer;
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }
    }

    class Program
    {
        private static void Write(ConsoleColor color, string text)
        {
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = ConsoleColor.White;
        }

        static void Main(string[] args)
        {
            Console.Write("Enter timing interval (ms): ");

            var intervalInput = Console.ReadLine();
            var interval = 0;

            if (!Int32.TryParse(intervalInput, out interval))
            {
                interval = 1000;
            }

            var packetTester = new PacketLossTest(new[] { "192.168.1.1", "netflix.com", "www.google.com", "www.yahoo.com", "www.reddit.com", "www.microsoft.com", "cox.net", "bing.com", "amazon.com", "www.amazon.com", "8.8.8.8", "8.8.4.4", "stackoverflow.com", "gmail.com" }, interval);

            var startTime = DateTime.Now;
            var updateScreenTimer = new Timer(5000);

            updateScreenTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
            {
                Console.Clear();

                var totalPackets = 0;
                var totalFailures = 0;

                var packets = packetTester.Responses.OrderBy(r => r.Value.Responses.Count(re => re.Status == IPStatus.Success));

                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("Test timing interval: {0} ms ({1:0.00} s)\n", packetTester.GetInterval(), Convert.ToDecimal(packetTester.GetInterval()) / 1000);
                Console.ForegroundColor = ConsoleColor.White;

                foreach (var r in packets)
                {
                    var total = r.Value.Responses.Count();
                    var failures = r.Value.GetFailedResponseCount();

                    totalPackets += total;
                    totalFailures += failures;

                    Console.WriteLine("Host: {0,-20} Success: {1,-5} Fail: {2,-5} Avg: {3,-8:###.##} Loss %: {4,-8:###.##} Jitt: {5:###.##}", r.Key, total, failures, r.Value.GetAverageResponseTime(), failures == 0 || total == 0 ? 0 : (Convert.ToDecimal(failures) / Convert.ToDecimal(total)) * 100, r.Value.GetJitter(50));

                    Console.ForegroundColor = ConsoleColor.White;
                }

                Console.WriteLine();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Total Requests: {0,-10} Total Success: {1, -10} Total Failures: {2} ({3:0.00}%)\n", totalPackets, totalPackets - totalFailures, totalFailures, Math.Round((Convert.ToDecimal(totalFailures) / Convert.ToDecimal(totalPackets)) * 100, 2));
                Console.ForegroundColor = ConsoleColor.White;

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Elapsed Time: {0}m {1:#}s", ((TimeSpan)(DateTime.Now - startTime)).TotalMinutes < 0 ? 0 : Math.Round(((TimeSpan)(DateTime.Now - startTime)).TotalMinutes, 0), ((TimeSpan)(DateTime.Now - startTime)).TotalSeconds % 60);
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine();
                Console.WriteLine("Press ESC to stop.");
            };

            updateScreenTimer.Start();
            System.Threading.Thread.Sleep(2500);
            packetTester.Start();

            do
            {
            } while (Console.ReadKey(true).Key != ConsoleKey.Escape);

            packetTester.Stop();
            updateScreenTimer.Stop();

            Console.WriteLine("Press ENTER to close.");

            Console.Read();
        }
    }
}
