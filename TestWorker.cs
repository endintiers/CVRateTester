using ComposableAsync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RateLimiter;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CVRateTester
{
    public class TestWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly int _workerId;
        private readonly TimeLimiter _timeLimiter;
        private readonly HttpClient _client;
        private readonly string _serviceUrl;

        private ILogger<TestWorker> _logger;

        private ByteArrayContent _imageContent;

        public TestWorker(IServiceProvider serviceProvider, int workerId, TimeLimiter timeLimiter, ByteArrayContent imageContent)
        {
            _serviceProvider = serviceProvider;
            _workerId = workerId;
            _timeLimiter = timeLimiter;
            _logger = _serviceProvider.GetRequiredService<ILogger<TestWorker>>();
            _imageContent = imageContent;

            var config = _serviceProvider.GetRequiredService<IConfiguration>();
            var cvEndPoint = config["CogVision:EndPoint"];
            var cvKey = config["CogVision:Key"];

            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", cvKey);
            _serviceUrl = $"{cvEndPoint}/vision/v3.0/read/analyze";

        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var rand = new Random(_workerId);
            var sleepFor = rand.Next(100, 3000);
            Thread.Sleep(sleepFor);

            _logger.LogInformation($"Starting TestWorker {_workerId}");

            // Make a Scope and Get the services we need
            using (var scope = _serviceProvider.CreateScope())
            {
                // No services needed here...

                // Now loop until cancelled 
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        try
                        {
                            _logger.LogInformation($"Starting Call to Cog Vision Read API Worker: {_workerId}");
                            string operationLocation;
                            int fourTwoNineCount = 0;
                            await _timeLimiter;
                            HttpResponseMessage response = await _client.PostAsync(_serviceUrl, _imageContent);
                            while (response.StatusCode == HttpStatusCode.TooManyRequests)
                            {
                                fourTwoNineCount++;
                                if (fourTwoNineCount > 10)
                                {
                                    // Something very wrong
                                    throw new ApplicationException($"Post to Cog Vision Read API failed, looping on TooManyRequests, 429 Count: {fourTwoNineCount}");
                                }
                                // Try again once after a second
                                Thread.Sleep(1000);
                                await _timeLimiter;
                                response = await _client.PostAsync(_serviceUrl, _imageContent);
                            }
                            if (response.IsSuccessStatusCode)
                            {
                                operationLocation =
                                    response.Headers.GetValues("Operation-Location").FirstOrDefault();

                                string contentString;
                                int i = 0;
                                do
                                {
                                    Thread.Sleep(1000); // Wait 1 sec to check if the operation finishes
                                    await _timeLimiter;
                                    response = await _client.GetAsync(operationLocation);
                                    //await _timeLimiter;
                                    //Thread.Sleep(1000);
                                    contentString = await response.Content.ReadAsStringAsync();
                                    if (contentString.IndexOf("\"error\":") > -1)
                                    {
                                        if (contentString.IndexOf("\"code\":\"429\"") > -1)
                                        {
                                            fourTwoNineCount++;
                                        }
                                        else
                                        {
                                            _logger.LogInformation($"Call to Cog Vision Read API failed Worker: {_workerId}: {contentString}");
                                        }
                                    }
                                    ++i;
                                }
                                while (i < 60 && contentString.IndexOf("\"status\":\"succeeded\"") == -1);
                                if (i > 59 && contentString.IndexOf("\"status\":\"succeeded\"") == -1)
                                {
                                    throw new ApplicationException($"Call to Cog Vision Read API failed: Timeout on read result Worker: {_workerId}, 429 Count: {fourTwoNineCount}");
                                }
                                else
                                {
                                    _logger.LogInformation($"Call to Cog Vision Read API Succeeded Worker: {_workerId}, 429 Count: {fourTwoNineCount}");
                                }
                            } // End response.IsSuccessStatusCode
                            else
                            {
                                //await _timeLimiter;
                                string errorString = await response.Content.ReadAsStringAsync();
                                _logger.LogInformation($"Call to Cog Vision Read API failed Worker: {_workerId}: {JToken.Parse(errorString)}");
                            }

                        }
                        catch (Exception ex)
                        {
                            throw;
                        }

                        // Sleep a bit before trying again
                        await Task.Delay(1000, stoppingToken);

                    }
                    catch (TaskCanceledException)
                    {
                    }
                }
            }
        }
    }
}
