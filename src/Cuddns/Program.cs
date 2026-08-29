using Cuddns;
using Cuddns.Cache;
using Cuddns.Config;
using Cuddns.Orchestration;
using Cuddns.Providers;
using Cuddns.Providers.DuckDns;
using Cuddns.Providers.Route53;
using Cuddns.PublicIp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var configPath = Environment.GetEnvironmentVariable("CUDDNS_CONFIG_PATH") ?? "/config/config.yaml";
var envPath = Environment.GetEnvironmentVariable("CUDDNS_ENV_PATH") ?? "/config/.env";
var cachePath = Environment.GetEnvironmentVariable("CUDDNS_CACHE_PATH") ?? "/data/cache.json";

// A short-lived logger for the pre-host bootstrap phase below (catalog/provider setup
// happens before the DI container exists yet). The app's real logging pipeline takes
// over once the host is built further down.
using var startupLoggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole());
var startupLogger = startupLoggerFactory.CreateLogger("Cuddns.Startup");

// The provider catalog: every provider type Cuddns ships with. Adding a new provider means
// adding one entry here. Only the types actually referenced in config get instantiated below.
IDnsProviderFactory[] catalog =
[
    new Route53DnsProviderFactory(startupLoggerFactory.CreateLogger<Route53DnsProviderFactory>()),
    new DuckDnsProviderFactory(startupLoggerFactory.CreateLogger<DuckDnsProviderFactory>()),
];
startupLogger.LogInformation("Available provider types: {ProviderTypes}", string.Join(", ", catalog.Select(f => f.ProviderType)));

var cuddnsOptions = new ConfigLoader(catalog).Load(configPath, envPath);
startupLogger.LogInformation("Configured providers: {ConfiguredTypes}", string.Join(", ", cuddnsOptions.Providers.Select(p => p.Type)));

var catalogByType = catalog.ToDictionary(f => f.ProviderType);
var dnsProviders = cuddnsOptions.Providers
    .Select(providerConfig => catalogByType[providerConfig.Type].Create(providerConfig))
    .ToList();

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

builder.Services.AddSingleton<IReadOnlyList<IDnsProvider>>(dnsProviders);

builder.Services.AddSingleton<DdnsUpdateService>();
builder.Services.AddHostedService<DdnsWorker>();

var host = builder.Build();
host.Services.GetRequiredService<ILogger<Program>>()
    .LogInformation("Cuddns {Version} starting", AppVersion.Current);
host.Run();
