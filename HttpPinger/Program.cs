using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Policy;
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
        static readonly string LogFile = Environment.ExpandEnvironmentVariables(@"%SystemDrive%\home\logFiles\HttpPinger.log");
        static readonly Lazy<string> Uris = new Lazy<string>(() => Environment.GetEnvironmentVariable("HTTPPINGER_URIS"));
        static readonly Lazy<string> IntervalSecs = new Lazy<string>(() => Environment.GetEnvironmentVariable("HTTPPINGER_INTERVALSECS"));
        static readonly Lazy<string> ExpiredSecs = new Lazy<string>(() => Environment.GetEnvironmentVariable("HTTPPINGER_EXPIREDSECS"));
        static TimeSpan _interval = TimeSpan.FromSeconds(300);

        static DateTime _logRetention = DateTime.UtcNow.AddSeconds(_interval.TotalSeconds * 3);

        static HashSet<string> _missingDns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static DateTime _expired = DateTime.UtcNow.AddHours(3);

        static void Main(string[] args)
        {
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };

            if (int.TryParse(IntervalSecs.Value, out int intervalSecs))
            {
                _interval = TimeSpan.FromSeconds(intervalSecs);
            }

            if (int.TryParse(ExpiredSecs.Value, out int expiredSecs))
            {
                _expired = DateTime.UtcNow.AddSeconds(expiredSecs);
            }

            while (DateTime.UtcNow < _expired)
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
                lock (_missingDns)
                {
                    if (_missingDns.Contains($"{uri}"))
                    {
                        return;
                    }
                }

                var startTime = DateTime.UtcNow;
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HttpPinger", "1.0"));
                    client.Timeout = TimeSpan.FromSeconds(10);
                    using (var response = await client.GetAsync(uri))
                    {
                        Log($"Ping '{uri}', Status {response.StatusCode}, Latency: {(int)(DateTime.UtcNow - startTime).TotalMilliseconds}ms");
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.ToString().Contains("The remote name could not be resolved"))
                {
                    lock (_missingDns)
                    {
                        if (!_missingDns.Contains($"{uri}"))
                        {
                            _missingDns.Add($"{uri}");
                        }
                    }
                }

                Log($"Ping '{uri}', {ex}");
            }
        }

        static void Log(string message)
        {
            var now = DateTime.UtcNow;
            if (File.Exists(LogFile))
            {
                if (_logRetention > now)
                {
                    lock (typeof(Console))
                    {
                        File.AppendAllLines(LogFile, new[] { $"{DateTime.UtcNow:s} {message}" });
                    }
                }
            }
            else
            {
                _logRetention = now.AddSeconds(_interval.TotalSeconds * 3);
            }
        }
    }
}
