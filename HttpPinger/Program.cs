using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace HttpPinger
{
    class Program
    {
        static readonly HashSet<string> UriFormats = new HashSet<string>(new[]
        {
            "http://{0}.azurewebsites.net",
            "http://{0}.scm.azurewebsites.net",
            "http://{0}.kudu1.antares-test.windows-int.net",
            "http://{0}.scm.kudu1.antares-test.windows-int.net",
        });
        static readonly Lazy<string> LogFile = new Lazy<string>(() => Environment.GetEnvironmentVariable("HTTPPINGER_LOGFILE"));
        static readonly Lazy<string> Uris = new Lazy<string>(() => Environment.GetEnvironmentVariable("HTTPPINGER_URIS"));
        static readonly Lazy<string> IntervalSecs = new Lazy<string>(() => Environment.GetEnvironmentVariable("HTTPPINGER_INTERVALSECS"));
        static TimeSpan _interval = TimeSpan.FromSeconds(300);

        static void Main(string[] args)
        {
            if (int.TryParse(IntervalSecs.Value, out int intervalSecs))
            {
                _interval = TimeSpan.FromSeconds(intervalSecs);
            }

            while (true)
            {
                try
                {
                    List<Task> tasks = new List<Task>();
                    foreach (var setting in Uris.Value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (Uri.TryCreate(setting, UriKind.Absolute, out Uri uri))
                        {
                            tasks.Add(Ping(uri));
                        }
                        else
                        {
                            tasks.AddRange(UriFormats.Select(format =>
                            {
                                Console.WriteLine(string.Format(format, setting));
                                return Ping(new Uri(string.Format(format, setting)));
                            }));
                        }
                    }

                    Task.WaitAll(tasks.ToArray());
                }
                catch (Exception ex)
                {
                    Log($"HTTPPINGER_URIS: '{Uris.Value}', {ex}");
                }

                Thread.Sleep(_interval);
            }
        }

        static async Task Ping(Uri uri)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HttpPinger", "1.0"));
                    client.Timeout = _interval;
                    using (var response = await client.GetAsync(uri))
                    {
                        Log($"Ping '{uri}', Status {response.StatusCode}, Latency: {(int)(DateTime.UtcNow - startTime).TotalMilliseconds}ms");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Ping '{uri}', {ex}");
            }
        }

        static void Log(string message)
        {
            var logFile = LogFile.Value;
            if (!string.IsNullOrEmpty(logFile))
            {
                lock (typeof(Console))
                {
                    File.AppendAllLines(logFile, new[] { $"{DateTime.UtcNow:s} {message}" });
                }
            }
        }
    }
}
