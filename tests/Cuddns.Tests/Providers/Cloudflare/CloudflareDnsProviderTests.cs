using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Cuddns.Providers;
using Cuddns.Providers.Cloudflare;
using FluentAssertions;

namespace Cuddns.Tests.Providers.Cloudflare;

public class CloudflareDnsProviderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri? Uri, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri, body));
            return respond(request, body);
        }
    }

    private static CloudflareProviderConfig BuildConfig(params (string ZoneId, int Ttl, bool Proxied, string[] Records)[] zones)
    {
        return new CloudflareProviderConfig
        {
            ApiToken = "test-token",
            Zones = zones.Select(z => new CloudflareZoneConfig
            {
                ZoneId = z.ZoneId,
                Ttl = z.Ttl,
                Proxied = z.Proxied,
                Records = [.. z.Records],
            }).ToList(),
        };
    }

    private static HttpResponseMessage JsonResponse(object body) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    [Fact]
    public void ManagedRecords_FlattensAllZonesWithTheirOwnTtl()
    {
        var config = BuildConfig(
            ("zone1", 300, false, ["a.example.com"]),
            ("zone2", 60, true, ["b.example.com"]));

        var provider = new CloudflareDnsProvider(new HttpClient(), config);

        provider.ManagedRecords.Should().BeEquivalentTo(
        [
            new ManagedRecord("a.example.com", 300),
            new ManagedRecord("b.example.com", 60),
        ]);
    }

    [Fact]
    public async Task UpsertRecordAsync_NoExistingRecord_CreatesWithPost()
    {
        var config = BuildConfig(("zone1", 300, true, ["a.example.com"]));
        var handler = new StubHandler((request, _) =>
            request.Method == HttpMethod.Get
                ? JsonResponse(new { success = true, result = Array.Empty<object>() })
                : JsonResponse(new { success = true, result = new { id = "new-id" } }));
        using var httpClient = new HttpClient(handler);
        var provider = new CloudflareDnsProvider(httpClient, config);

        await provider.UpsertRecordAsync(provider.ManagedRecords[0], "203.0.113.10", CancellationToken.None);

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].Uri!.ToString().Should().Contain("/zones/zone1/dns_records?type=A&name=a.example.com");

        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        handler.Requests[1].Uri!.ToString().Should().EndWith("/zones/zone1/dns_records");
        handler.Requests[1].Body.Should().Contain("\"content\":\"203.0.113.10\"")
            .And.Contain("\"proxied\":true")
            .And.Contain("\"ttl\":300");
    }

    [Fact]
    public async Task UpsertRecordAsync_ExistingRecord_UpdatesWithPut()
    {
        var config = BuildConfig(("zone1", 300, false, ["a.example.com"]));
        var handler = new StubHandler((request, _) =>
            request.Method == HttpMethod.Get
                ? JsonResponse(new { success = true, result = new[] { new { id = "existing-id" } } })
                : JsonResponse(new { success = true, result = new { id = "existing-id" } }));
        using var httpClient = new HttpClient(handler);
        var provider = new CloudflareDnsProvider(httpClient, config);

        await provider.UpsertRecordAsync(provider.ManagedRecords[0], "203.0.113.10", CancellationToken.None);

        handler.Requests[1].Method.Should().Be(HttpMethod.Put);
        handler.Requests[1].Uri!.ToString().Should().EndWith("/zones/zone1/dns_records/existing-id");
    }

    [Fact]
    public async Task UpsertRecordAsync_ApiReturnsFailure_ThrowsWithErrorMessage()
    {
        var config = BuildConfig(("zone1", 300, false, ["a.example.com"]));
        var handler = new StubHandler((request, _) =>
            request.Method == HttpMethod.Get
                ? JsonResponse(new { success = true, result = Array.Empty<object>() })
                : JsonResponse(new { success = false, errors = new[] { new { message = "boom" } } }));
        using var httpClient = new HttpClient(handler);
        var provider = new CloudflareDnsProvider(httpClient, config);

        var act = () => provider.UpsertRecordAsync(provider.ManagedRecords[0], "203.0.113.10", CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*boom*");
    }
}
