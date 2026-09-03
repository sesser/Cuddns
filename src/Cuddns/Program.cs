using Cuddns;
using Cuddns.Cache;
using Cuddns.Config;
using Cuddns.Logging;
using Cuddns.Orchestration;
using Cuddns.Providers;
using Cuddns.Providers.Cloudflare;
using Cuddns.Providers.DuckDns;
using Cuddns.Providers.Miab;
using Cuddns.Providers.NoIp;
using Cuddns.Providers.Route53;
using Cuddns.PublicIp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

var configPath = Environment.GetEnvironmentVariable("CUDDNS_CONFIG_PATH") ?? "/config/config.yaml";
var envPath = Environment.GetEnvironmentVariable("CUDDNS_ENV_PATH") ?? "/config/.env";
var cachePath = Environment.GetEnvironmentVariable("CUDDNS_CACHE_PATH") ?? "/data/cache.json";

// A short-lived logger for the pre-host bootstrap phase below (catalog/provider setup
// happens before the DI container exists yet). The app's real logging pipeline takes
// over once the host is built further down, configured the same way.
using var startupLoggerFactory = LoggerFactory.Create(ConfigureConsoleLogging);
var startupLogger = startupLoggerFactory.CreateLogger("Cuddns.Startup");

// The provider catalog: every provider type Cuddns ships with. Adding a new provider means
// adding one entry here. Only the types actually referenced in config get instantiated below.
IDnsProviderFactory[] catalog =
[
    new Route53DnsProviderFactory(startupLoggerFactory.CreateLogger<Route53DnsProviderFactory>()),
    new DuckDnsProviderFactory(startupLoggerFactory.CreateLogger<DuckDnsProviderFactory>()),
    new CloudflareDnsProviderFactory(startupLoggerFactory.CreateLogger<CloudflareDnsProviderFactory>()),
    new NoIpProviderFactory(startupLoggerFactory.CreateLogger<NoIpProviderFactory>()),
    new MiabDnsProviderFactory(startupLoggerFactory.CreateLogger<MiabDnsProviderFactory>()),
];
startupLogger.LogInformation("Available provider types: {ProviderTypes}", string.Join(", ", catalog.Select(f => f.ProviderType)));

var catalogByType = catalog.ToDictionary(f => f.ProviderType);
var configLoader = new ConfigLoader(catalog);

var builder = Host.CreateApplicationBuilder(args);

// Replace the default console provider (its two-line "info: Category[0]\n      message"
// format with no timestamp) with a single-line, timestamped one.
builder.Logging.ClearProviders();
ConfigureConsoleLogging(builder.Logging);

builder.Services.AddHttpClient<IfConfigNetPublicIpSource>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    // ifconfig.net returns a full HTML page instead of the plain-text IP for clients it
    // doesn't recognize as a CLI tool (the default HttpClient sends no User-Agent at all).
    client.DefaultRequestHeaders.UserAgent.ParseAdd("curl/8.5.0");
});
builder.Services.AddHttpClient<IpifyPublicIpSource>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient<IcanhazipPublicIpSource>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient<IdentMePublicIpSource>(client => client.Timeout = TimeSpan.FromSeconds(10));

builder.Services.AddSingleton<IIpCacheStore>(sp =>
    new JsonFileIpCacheStore(cachePath, sp.GetRequiredService<ILogger<JsonFileIpCacheStore>>()));

// Builds a ConfigSnapshot (options + providers + resolver) from config.yaml/.env — used both
// for the initial load below and by ConfigWatcherService on every hot-reload attempt, so a
// reload behaves identically to a fresh start.
builder.Services.AddSingleton(sp => new ConfigSnapshotBuilder(
    configLoader,
    catalogByType,
    new Dictionary<string, IPublicIpSource>
    {
        [PublicIpSourceNames.IfConfig] = sp.GetRequiredService<IfConfigNetPublicIpSource>(),
        [PublicIpSourceNames.Ipify] = sp.GetRequiredService<IpifyPublicIpSource>(),
        [PublicIpSourceNames.Icanhazip] = sp.GetRequiredService<IcanhazipPublicIpSource>(),
        [PublicIpSourceNames.IdentMe] = sp.GetRequiredService<IdentMePublicIpSource>(),
    },
    sp.GetRequiredService<ILogger<PublicIpResolver>>()));

// Loads config.yaml/.env now — same fail-fast timing as before this feature existed: an
// invalid config throws here, before the host starts anything.
builder.Services.AddSingleton(sp =>
    new ConfigState(sp.GetRequiredService<ConfigSnapshotBuilder>().Build(configPath, envPath)));

builder.Services.AddSingleton<DdnsUpdateService>();
builder.Services.AddHostedService<DdnsWorker>();
builder.Services.AddHostedService(sp => new ConfigWatcherService(
    sp.GetRequiredService<ConfigSnapshotBuilder>(),
    sp.GetRequiredService<ConfigState>(),
    configPath,
    envPath,
    sp.GetRequiredService<ILogger<ConfigWatcherService>>()));

var host = builder.Build();

var initialOptions = host.Services.GetRequiredService<ConfigState>().Current.Options;
host.Services.GetRequiredService<ILogger<Program>>().LogInformation(
    "Cuddns {Version} starting (providers: {ConfiguredTypes})",
    AppVersion.Current, string.Join(", ", initialOptions.Providers.Select(p => p.Type)));

host.Run();

static void ConfigureConsoleLogging(ILoggingBuilder logging)
{
    logging.AddConsole(options => options.FormatterName = "cuddns")
        .AddConsoleFormatter<CuddnsConsoleFormatter, ConsoleFormatterOptions>();
}
