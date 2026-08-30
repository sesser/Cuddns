namespace Cuddns.Providers;

/// <summary>A DNS record a provider manages, as exposed to the orchestrator.</summary>
public sealed record ManagedRecord(string Name, int Ttl, RecordType Type = RecordType.A);
