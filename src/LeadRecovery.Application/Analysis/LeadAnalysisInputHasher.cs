using System.Security.Cryptography;
using System.Text.Json;

namespace LeadRecovery.Application.Analysis;

public interface ILeadAnalysisInputHasher
{
    string ComputeHash(LeadAnalysisRequest request);
}

public sealed class LeadAnalysisInputHasher : ILeadAnalysisInputHasher
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public string ComputeHash(LeadAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        byte[] canonicalInput = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                request.SchemaVersion,
                request.AllowedCategories,
                turns = request.Turns.Select(turn => new
                {
                    participant = turn.Participant.ToString(),
                    turn.Text,
                }),
                request.ServiceAreaRules,
            },
            SerializerOptions);
        return Convert.ToHexString(SHA256.HashData(canonicalInput))
            .ToLowerInvariant();
    }
}
