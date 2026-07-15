using LeadRecovery.Domain.Conversations;

namespace LeadRecovery.Domain.Tests;

public sealed class MessageTemplateTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 15, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OnlyApprovedTemplateCanBeActivated()
    {
        MessageTemplate template = CreateTemplate();

        Assert.Throws<InvalidOperationException>(template.Activate);

        Guid approverId = Guid.CreateVersion7();
        template.Approve(approverId, Now);
        template.Activate();

        Assert.True(template.IsApproved);
        Assert.True(template.IsActive);
        Assert.Equal(approverId, template.ApprovedByUserId);
    }

    [Fact]
    public void ApprovedTemplateBodyAndVersionRemainImmutable()
    {
        MessageTemplate template = CreateTemplate();
        template.Approve(Guid.CreateVersion7(), Now);
        template.Activate();
        template.Deactivate();

        Assert.Equal("Hi from {{BusinessName}}", template.Body);
        Assert.Equal(1, template.Version);
        Assert.False(template.IsActive);
    }

    private static MessageTemplate CreateTemplate() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "Initial recovery",
        "InitialMissedCallRecovery",
        "Hi from {{BusinessName}}",
        1,
        Guid.CreateVersion7(),
        Now);
}
