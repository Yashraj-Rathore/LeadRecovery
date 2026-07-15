using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Domain.Tests;

public sealed class LeadNoteTests
{
    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.UnixEpoch;

    [Fact]
    public void NoteNormalizesBodyAndRequiresTenantLeadAndAuthor()
    {
        LeadNote note = new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "  Follow up after 3 PM.  ",
            CreatedAtUtc);

        Assert.Equal("Follow up after 3 PM.", note.Body);
        Assert.Throws<ArgumentException>(() => new LeadNote(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.Empty,
            "Note",
            CreatedAtUtc));
    }

    [Fact]
    public void NoteRejectsEmptyOversizedAndNonUtcBody()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid leadId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();

        Assert.Throws<ArgumentException>(() => new LeadNote(
            Guid.CreateVersion7(),
            tenantId,
            leadId,
            userId,
            " ",
            CreatedAtUtc));
        Assert.Throws<ArgumentException>(() => new LeadNote(
            Guid.CreateVersion7(),
            tenantId,
            leadId,
            userId,
            new string('x', LeadNoteFieldLimits.BodyMaximumLength + 1),
            CreatedAtUtc));
        Assert.Throws<ArgumentException>(() => new LeadNote(
            Guid.CreateVersion7(),
            tenantId,
            leadId,
            userId,
            "Note",
            CreatedAtUtc.ToOffset(TimeSpan.FromHours(-4))));
    }
}
