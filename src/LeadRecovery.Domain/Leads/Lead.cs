namespace LeadRecovery.Domain.Leads;

public sealed class Lead
{
    private Lead()
    {
    }

    public Lead(
        Guid id,
        Guid tenantId,
        string primaryPhoneE164,
        LeadSource source,
        DateTimeOffset createdAtUtc,
        string? displayName = null)
    {
        Id = RequireId(id, nameof(id));
        TenantId = RequireId(tenantId, nameof(tenantId));
        PrimaryPhoneE164 = NormalizeRequired(
            primaryPhoneE164,
            LeadFieldLimits.PrimaryPhoneMaximumLength,
            nameof(primaryPhoneE164));
        DisplayName = NormalizeOptional(
            displayName,
            LeadFieldLimits.DisplayNameMaximumLength,
            nameof(displayName));
        Source = RequireDefined(source, nameof(source));
        Status = LeadStatus.New;
        Urgency = LeadUrgency.Unknown;
        AutomationState = AutomationState.Active;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid? CustomerId { get; private set; }

    public string PrimaryPhoneE164 { get; private set; } = string.Empty;

    public string? DisplayName { get; private set; }

    public LeadSource Source { get; private set; }

    public LeadStatus Status { get; private set; }

    public LeadUrgency Urgency { get; private set; }

    public Guid? ServiceCategoryId { get; private set; }

    public Guid? AssignedUserId { get; private set; }

    public AutomationState AutomationState { get; private set; }

    public DateTimeOffset? LastCustomerActivityAtUtc { get; private set; }

    public DateTimeOffset? LastBusinessActivityAtUtc { get; private set; }

    public DateTimeOffset? BookedAtUtc { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public LeadCloseReason? CloseReason { get; private set; }

    public long Version { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void BeginContacting(DateTimeOffset changedAtUtc) =>
        TransitionTo(LeadStatus.Contacting, changedAtUtc);

    public void AwaitCustomer(DateTimeOffset changedAtUtc) =>
        TransitionTo(LeadStatus.AwaitingCustomer, changedAtUtc);

    public void Qualify(
        bool minimumRequiredDetailsPresent,
        string? staffOverrideReason,
        DateTimeOffset changedAtUtc)
    {
        EnsureCanTransitionTo(LeadStatus.Qualified);

        if (!minimumRequiredDetailsPresent)
        {
            _ = NormalizeRequired(
                staffOverrideReason,
                LeadFieldLimits.StaffOverrideReasonMaximumLength,
                nameof(staffOverrideReason));
        }

        ApplyTransition(LeadStatus.Qualified, changedAtUtc);
    }

    public void OfferBooking(DateTimeOffset changedAtUtc) =>
        TransitionTo(LeadStatus.BookingOffered, changedAtUtc);

    public void RequireHumanReview(DateTimeOffset changedAtUtc) =>
        TransitionTo(LeadStatus.NeedsHuman, changedAtUtc);

    public void Book(DateTimeOffset bookedAtUtc)
    {
        EnsureCanTransitionTo(LeadStatus.Booked);
        DateTimeOffset utcTimestamp = RequireCurrentOrLaterUtc(
            bookedAtUtc,
            nameof(bookedAtUtc));

        Status = LeadStatus.Booked;
        AutomationState = AutomationState.Completed;
        BookedAtUtc = utcTimestamp;
        UpdatedAtUtc = utcTimestamp;
    }

    public void ConfirmWon(DateTimeOffset confirmedAtUtc)
    {
        EnsureCanTransitionTo(LeadStatus.ClosedWon);
        DateTimeOffset utcTimestamp = RequireCurrentOrLaterUtc(
            confirmedAtUtc,
            nameof(confirmedAtUtc));

        Status = LeadStatus.ClosedWon;
        ClosedAtUtc = utcTimestamp;
        UpdatedAtUtc = utcTimestamp;
    }

    public void Close(LeadCloseReason reason, DateTimeOffset closedAtUtc)
    {
        LeadCloseReason definedReason = RequireDefined(reason, nameof(reason));
        EnsureCanTransitionTo(LeadStatus.Closed);
        DateTimeOffset utcTimestamp = RequireCurrentOrLaterUtc(
            closedAtUtc,
            nameof(closedAtUtc));

        Status = LeadStatus.Closed;
        CloseReason = definedReason;
        ClosedAtUtc = utcTimestamp;
        AutomationState = definedReason == LeadCloseReason.OptedOut
            ? AutomationState.SuppressedOptOut
            : AutomationState.Completed;
        UpdatedAtUtc = utcTimestamp;
    }

    private void TransitionTo(LeadStatus target, DateTimeOffset changedAtUtc)
    {
        EnsureCanTransitionTo(target);
        ApplyTransition(target, changedAtUtc);
    }

    private void EnsureCanTransitionTo(LeadStatus target)
    {
        if (!LeadStatusTransitionPolicy.CanTransition(Status, target))
        {
            throw new InvalidOperationException(
                $"A lead cannot transition from {Status} to {target}.");
        }
    }

    private void ApplyTransition(LeadStatus target, DateTimeOffset changedAtUtc)
    {
        DateTimeOffset utcTimestamp = RequireCurrentOrLaterUtc(
            changedAtUtc,
            nameof(changedAtUtc));
        Status = target;
        UpdatedAtUtc = utcTimestamp;
    }

    private static Guid RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty ID is required.", parameterName);
        }

        return value;
    }

    private static TEnum RequireDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    private static string NormalizeRequired(
        string? value,
        int maximumLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must be in UTC.", parameterName);
        }

        return value;
    }

    private DateTimeOffset RequireCurrentOrLaterUtc(
        DateTimeOffset value,
        string parameterName)
    {
        DateTimeOffset utcValue = RequireUtc(value, parameterName);
        if (utcValue < UpdatedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The update timestamp cannot move backwards.");
        }

        return utcValue;
    }
}
