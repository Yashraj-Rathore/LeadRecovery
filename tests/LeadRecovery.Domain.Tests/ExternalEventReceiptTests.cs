using LeadRecovery.Domain.Integrations;

namespace LeadRecovery.Domain.Tests;

public sealed class ExternalEventReceiptTests
{
    private static readonly DateTimeOffset ReceivedAtUtc =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorAllowsUnresolvedTenantAndNormalizesIdentity()
    {
        Guid id = Guid.CreateVersion7();

        ExternalEventReceipt receipt = new(
            id,
            null,
            " Twilio ",
            " CallStatus ",
            " event-123 ",
            " sha256:abc ",
            ReceivedAtUtc);

        Assert.Equal(id, receipt.Id);
        Assert.Null(receipt.TenantId);
        Assert.Equal("Twilio", receipt.Provider);
        Assert.Equal("CallStatus", receipt.EventType);
        Assert.Equal("event-123", receipt.ExternalEventId);
        Assert.Equal("sha256:abc", receipt.PayloadHash);
        Assert.Null(receipt.ProcessedAtUtc);
        Assert.Null(receipt.ProcessingResult);
    }

    [Fact]
    public void TenantCanBeAssignedOnceAndSameAssignmentIsIdempotent()
    {
        ExternalEventReceipt receipt = CreateReceipt();
        Guid tenantId = Guid.CreateVersion7();

        receipt.AssignTenant(tenantId);
        receipt.AssignTenant(tenantId);

        Assert.Equal(tenantId, receipt.TenantId);
        Assert.Throws<InvalidOperationException>(() =>
            receipt.AssignTenant(Guid.CreateVersion7()));
        Assert.Throws<ArgumentException>(() => receipt.AssignTenant(Guid.Empty));
    }

    [Fact]
    public void ReceiptCanBeMarkedProcessedOnce()
    {
        ExternalEventReceipt receipt = CreateReceipt();
        DateTimeOffset processedAtUtc = ReceivedAtUtc.AddSeconds(1);

        receipt.MarkProcessed(" accepted ", processedAtUtc);

        Assert.Equal("accepted", receipt.ProcessingResult);
        Assert.Equal(processedAtUtc, receipt.ProcessedAtUtc);
        Assert.Throws<InvalidOperationException>(() =>
            receipt.MarkProcessed("again", processedAtUtc));
    }

    [Fact]
    public void ProcessingRejectsInvalidValuesWithoutMutation()
    {
        ExternalEventReceipt receipt = CreateReceipt();

        Assert.Throws<ArgumentOutOfRangeException>(() => receipt.MarkProcessed(
            "accepted",
            ReceivedAtUtc.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => receipt.MarkProcessed(
            " ",
            ReceivedAtUtc));
        Assert.Throws<ArgumentException>(() => receipt.MarkProcessed(
            "accepted",
            ReceivedAtUtc.ToOffset(TimeSpan.FromHours(-5))));

        Assert.Null(receipt.ProcessedAtUtc);
        Assert.Null(receipt.ProcessingResult);
    }

    [Fact]
    public void ConstructorRejectsInvalidRequiredValues()
    {
        Assert.Throws<ArgumentException>(() => CreateReceipt(id: Guid.Empty));
        Assert.Throws<ArgumentException>(() => CreateReceipt(tenantId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => CreateReceipt(provider: " "));
        Assert.Throws<ArgumentException>(() => CreateReceipt(eventType: " "));
        Assert.Throws<ArgumentException>(() => CreateReceipt(externalEventId: " "));
        Assert.Throws<ArgumentException>(() => CreateReceipt(payloadHash: " "));
        Assert.Throws<ArgumentException>(() => CreateReceipt(
            provider: new string(
                'a',
                ExternalEventReceiptFieldLimits.ProviderMaximumLength + 1)));
        Assert.Throws<ArgumentException>(() => CreateReceipt(
            receivedAtUtc: ReceivedAtUtc.ToOffset(TimeSpan.FromHours(-5))));
    }

    private static ExternalEventReceipt CreateReceipt(
        Guid? id = null,
        Guid? tenantId = null,
        string provider = "Twilio",
        string eventType = "CallStatus",
        string externalEventId = "event-123",
        string payloadHash = "sha256:abc",
        DateTimeOffset? receivedAtUtc = null) =>
        new(
            id ?? Guid.CreateVersion7(),
            tenantId,
            provider,
            eventType,
            externalEventId,
            payloadHash,
            receivedAtUtc ?? ReceivedAtUtc);
}
