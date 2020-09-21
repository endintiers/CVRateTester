using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RateLimiter;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;

namespace CVRateTester
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    // Load up the image content we will use for the tests
                    ByteArrayContent content;
                    using (var fs = File.OpenRead(@".\Documents\Page11_Image1.jpg"))
                    {
                        byte[] byteData;
                        fs.Position = 0;
                        using (var binaryReader = new BinaryReader(fs))
                        {
                            byteData = binaryReader.ReadBytes((int)fs.Length);
                        }
                        content = new ByteArrayContent(byteData);
                        content.Headers.ContentType =
                            new MediaTypeHeaderValue("application/octet-stream");
                    }

                    // Set up a shared Time based RateLimiter (for all calls to service)
                    var callsPerSecond = 5;
                    Console.WriteLine($"TimeLimiter Max Calls/Sec: {callsPerSecond}");
                    var timeLimiter = TimeLimiter.GetFromMaxCountByInterval(callsPerSecond, TimeSpan.FromSeconds(1));

                    // Add 'n' Workers
                    var maxDOP = 20;
                    for (int i=0; i<maxDOP; i++)
                    {
                        int workerId = i;
                        services.AddSingleton<IHostedService>(
                            sp => new TestWorker(
                                serviceProvider: sp,
                                workerId: workerId,
                                timeLimiter: timeLimiter,
                                imageContent: content
                            )
                        );
                    }
                });
    }
}
