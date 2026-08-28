namespace Cuddns;

/// <summary>
/// The running application version, baked into the container image at build time from
/// the triggering git tag (see .github/workflows/docker-publish.yml and Dockerfile's
/// VERSION build-arg). Falls back to "dev" for local/non-container runs.
/// </summary>
public static class AppVersion
{
    public static string Current { get; } = Environment.GetEnvironmentVariable("CUDDNS_VERSION") ?? "dev";
}
