namespace LeadRecovery.Domain.Automations;

public static class WorkflowDefinitionFieldLimits
{
    public const int NameMaximumLength = 120;
    public const int BookingUrlMaximumLength = 2048;
    public const int PolicyJsonMaximumLength = 16_384;
    public const int QuestionKeyMaximumLength = 80;
    public const int QuestionPromptMaximumLength = 500;
    public const int AnswerValueMaximumLength = 500;
    public const int TemplatePurposeMaximumLength = 80;
    public const int MaximumQuestions = 10;
    public const int MaximumFollowUps = 3;
}
