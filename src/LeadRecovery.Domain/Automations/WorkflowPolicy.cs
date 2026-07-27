namespace LeadRecovery.Domain.Automations;

public enum QualificationAnswerKind
{
    RequiredText,
    Choice,
}

public sealed record QualificationQuestionPolicy(
    string Key,
    string Prompt,
    QualificationAnswerKind AnswerKind,
    string[] AllowedValues);

public sealed record BusinessDayHours(
    DayOfWeek Day,
    TimeOnly OpensAt,
    TimeOnly ClosesAt);

public sealed record BusinessHoursPolicy(
    BusinessDayHours[] Windows,
    bool UrgentHumanReviewAfterHours);

public sealed record FollowUpStepPolicy(
    int Sequence,
    int DelayMinutes,
    string TemplatePurpose);
