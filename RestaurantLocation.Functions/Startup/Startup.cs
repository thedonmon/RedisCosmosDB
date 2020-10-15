using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using System;
using System.IO;
using System.Reflection;

[assembly: FunctionsStartup(typeof(RestaurantLocation.Functions.Startup.Startup))]

namespace RestaurantLocation.Functions.Startup
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            var actual_root = Environment.GetEnvironmentVariable("AzureWebJobsScriptRoot")  // local_root
                    ?? (Environment.GetEnvironmentVariable("HOME") == null
                        ? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                        : $"{Environment.GetEnvironmentVariable("HOME")}/site/wwwroot"); // azure_root
            var configBldr = new ConfigurationBuilder()
              .SetBasePath(actual_root)
              .AddJsonFile("local.settings.json", optional: true)
              .AddJsonFile("functionsettings.json", optional: false);

            var config = configBldr.Build();

            builder.Services.AddDistributedRedisCache((o) =>
            {
                o.ConfigurationOptions = ConfigurationOptions.Parse(config["RedisAzureRedisCache"]);

            });

        }
    }

}

