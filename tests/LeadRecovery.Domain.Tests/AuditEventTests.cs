using LeadRecovery.Domain.Audit;

namespace LeadRecovery.Domain.Tests;

public sealed class AuditEventTests
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 7, 14, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorCreatesNormalizedAuditRecord()
    {
        Guid id = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();

        AuditEvent auditEvent = new(
            id,
            tenantId,
            " User ",
            " actor ",
            " Authentication.Login ",
            " Session ",
            " entity ",
            " correlation ",
            CreatedAtUtc,
            "{}",
            "{}");

        Assert.Equal(id, auditEvent.Id);
        Assert.Equal(tenantId, auditEvent.TenantId);
        Assert.Equal("User", auditEvent.ActorType);
        Assert.Equal("actor", auditEvent.ActorId);
        Assert.Equal("Authentication.Login", auditEvent.Action);
        Assert.Equal("Session", auditEvent.EntityType);
        Assert.Equal("entity", auditEvent.EntityId);
        Assert.Equal("correlation", auditEvent.CorrelationId);
        Assert.Equal("{}", auditEvent.BeforeJson);
        Assert.Equal("{}", auditEvent.AfterJson);
    }

    [Fact]
    public void ConstructorAllowsSystemEventWithoutTenantOrActor()
    {
        AuditEvent auditEvent = new(
            Guid.CreateVersion7(),
            null,
            "System",
            null,
            "System.Started",
            "Application",
            "LeadRecovery.Api",
            "correlation",
            CreatedAtUtc);

        Assert.Null(auditEvent.TenantId);
        Assert.Null(auditEvent.ActorId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("not-json")]
    public void ConstructorRejectsInvalidJson(string json)
    {
        Assert.Throws<ArgumentException>(() => CreateAudit(beforeJson: json));
    }

    [Fact]
    public void ConstructorRejectsInvalidIdentityAndTimestamp()
    {
        Assert.Throws<ArgumentException>(() => CreateAudit(id: Guid.Empty));
        Assert.Throws<ArgumentException>(() => CreateAudit(tenantId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => CreateAudit(actorType: " "));
        Assert.Throws<ArgumentException>(() => CreateAudit(
            createdAtUtc: CreatedAtUtc.ToOffset(TimeSpan.FromHours(-4))));
    }

    private static AuditEvent CreateAudit(
        Guid? id = null,
        Guid? tenantId = null,
        string actorType = "User",
        DateTimeOffset? createdAtUtc = null,
        string? beforeJson = null) =>
        new(
            id ?? Guid.CreateVersion7(),
            tenantId,
            actorType,
            "actor",
            "Authentication.Login",
            "Session",
            "entity",
            "correlation",
            createdAtUtc ?? CreatedAtUtc,
            beforeJson);
}
