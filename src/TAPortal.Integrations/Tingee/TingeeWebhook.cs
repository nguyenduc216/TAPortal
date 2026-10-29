namespace TAPortal.Integrations.Tingee;

public enum TingeeWebhookKind
{
    Payment,
    VaStatus,
    BankLink
}

/// <summary>A signature-verified webhook as received. The raw body is kept verbatim for audit and replay.</summary>
public sealed record TingeeWebhookEnvelope(
    TingeeWebhookKind Kind,
    string? RequestId,
    string RequestTimestamp,
    string RawBody,
    DateTimeOffset ReceivedAtUtc);

/// <summary>Durable inbox. Must persist the envelope (idempotently) before the endpoint acknowledges Tingee.</summary>
public interface ITingeeWebhookInbox
{
    bool IsAvailable { get; }
    Task SaveAsync(TingeeWebhookEnvelope envelope, CancellationToken cancellationToken);
}

/// <summary>Placeholder until the inbox table is migrated: never acknowledges, so Tingee keeps retrying.</summary>
public sealed class UnavailableTingeeWebhookInbox : ITingeeWebhookInbox
{
    public bool IsAvailable => false;
    public Task SaveAsync(TingeeWebhookEnvelope envelope, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Tingee webhook inbox is not configured.");
}
