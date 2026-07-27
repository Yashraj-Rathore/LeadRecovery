using LeadRecovery.Domain.Automations;
using LeadRecovery.Domain.Leads;

namespace LeadRecovery.Application.Automations;

public sealed record QualificationEvaluation(
    QualificationAnswerOutcome Outcome,
    string? Value);

public interface IQualificationEvaluator
{
    QualificationEvaluation Evaluate(
        QualificationQuestionPolicy question,
        string response);
}

public sealed class QualificationEvaluator : IQualificationEvaluator
{
    public QualificationEvaluation Evaluate(
        QualificationQuestionPolicy question,
        string response)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentException.ThrowIfNullOrWhiteSpace(response);
        string normalized = response.Trim();
        if (normalized.Length > WorkflowDefinitionFieldLimits.AnswerValueMaximumLength)
        {
            return new QualificationEvaluation(QualificationAnswerOutcome.Unknown, null);
        }

        if (question.AnswerKind == QualificationAnswerKind.RequiredText)
        {
            return new QualificationEvaluation(
                QualificationAnswerOutcome.Accepted,
                normalized);
        }

        string? exact = question.AllowedValues.SingleOrDefault(
            value => value.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return new QualificationEvaluation(QualificationAnswerOutcome.Accepted, exact);
        }

        string[] matches = question.AllowedValues
            .Where(value => normalized.Contains(value, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length switch
        {
            1 => new QualificationEvaluation(QualificationAnswerOutcome.Accepted, matches[0]),
            > 1 => new QualificationEvaluation(QualificationAnswerOutcome.Ambiguous, null),
            _ => new QualificationEvaluation(QualificationAnswerOutcome.Unknown, null),
        };
    }
}
