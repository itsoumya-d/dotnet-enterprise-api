namespace OrdersApi.Models;

public record CodeChangeFile(
    string Path,
    int Additions,
    int Deletions,
    string PatchContent
);

public record CodeChangeReviewRequest(
    string PullRequestId,
    string Squad,
    string Author,
    bool HasAiAssistance,
    bool IsRegulatedFinancialSurface,
    List<CodeChangeFile> Files
);

public enum FindingSeverity
{
    Info,
    Warning,
    Error
}

public enum RuleCategory
{
    Architecture,
    Security,
    Resilience,
    Testing,
    AiGovernance
}

public record PracticeFinding(
    string RuleId,
    RuleCategory Category,
    FindingSeverity Severity,
    string Message,
    string RemediationSuggestion,
    string? FilePath = null,
    int? LineNumber = null
);

public record PracticeReviewSummary(
    string PullRequestId,
    string Squad,
    bool IsApproved,
    int QualityScore,
    int ErrorCount,
    int WarningCount,
    List<PracticeFinding> Findings,
    DateTime EvaluatedAtUtc
);

public record DoraDeploymentEvent(
    string DeploymentId,
    string Squad,
    string ServiceName,
    string Environment,
    string CommitSha,
    double LeadTimeMinutes,
    bool IsChangeFailure,
    DateTime DeployedAtUtc
);

public record DoraMetricsReport(
    string Squad,
    int TotalDeployments,
    double DeploymentFrequencyPerWeek,
    double MedianLeadTimeHours,
    double ChangeFailureRatePercent,
    string PerformanceTier,
    DateTime GeneratedAtUtc
);

public record EngineeringStandard(
    string Id,
    string Title,
    RuleCategory Category,
    string Description,
    string EnforcementMode,
    string ComplianceReference
);
