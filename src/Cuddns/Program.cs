using Cuddns;
using Cuddns.Cache;
using Cuddns.Config;
using Cuddns.Options;
using Cuddns.Orchestration;
using Cuddns.Providers;
using Cuddns.Providers.Route53;
using Cuddns.PublicIp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var configPath = Environment.GetEnvironmentVariable("CUDDNS_CONFIG_PATH") ?? "/config/config.yaml";
var envPath = Environment.GetEnvironmentVariable("CUDDNS_ENV_PATH") ?? "/config/.env";
var cachePath = Environment.GetEnvironmentVariable("CUDDNS_CACHE_PATH") ?? "/data/cache.json";

var cuddnsOptions = new ConfigLoader().Load(configPath, envPath);

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(cuddnsOptions);
builder.Services.AddHttpClient<IPublicIpProvider, IfConfigNetPublicIpProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    // ifconfig.net returns a full HTML page instead of the plain-text IP for clients it
    // doesn't recognize as a CLI tool (the default HttpClient sends no User-Agent at all).
    client.DefaultRequestHeaders.UserAgent.ParseAdd("curl/8.5.0");
});
builder.Services.AddSingleton<IIpCacheStore>(sp =>
    new JsonFileIpCacheStore(cachePath, sp.GetRequiredService<ILogger<JsonFileIpCacheStore>>()));

builder.Services.AddSingleton<IReadOnlyDictionary<string, IDnsProviderFactory>>(_ =>
{
    IDnsProviderFactory[] factories = [new Route53DnsProviderFactory()];
    return factories.ToDictionary(f => f.ProviderType, f => f);
});

builder.Services.AddSingleton<DdnsUpdateService>();
builder.Services.AddHostedService<DdnsWorker>();

var host = builder.Build();
host.Services.GetRequiredService<ILogger<Program>>()
    .LogInformation("Cuddns {Version} starting", AppVersion.Current);
host.Run();
