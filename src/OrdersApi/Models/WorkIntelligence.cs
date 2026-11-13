namespace OrdersApi.Models;

public record LeaseClause(
    string ClauseId,
    string SectionNumber,
    string Category,
    string Title,
    string Summary,
    string VerbatimExcerpt,
    bool IsLandlordLiability,
    bool RequiresFormalNotice
);

public record TenantCommunicationRequest(
    string PropertyId,
    string TenantId,
    string SenderEmail,
    string Subject,
    string MessageBody,
    string Channel = "Email",
    string? IdempotencyKey = null
);

public record GroundedContext(
    IReadOnlyList<LeaseClause> MatchedClauses,
    double GroundingConfidence,
    string RiskTier,
    IReadOnlyList<string> PrecedentDecisions
);

public record DraftActionItem(
    string ActionType,
    string Description,
    string TargetParty,
    string EstimatedResolutionDays
);

public record GroundedDraftResponse(
    string DraftBody,
    IReadOnlyList<string> CitedSections,
    IReadOnlyList<DraftActionItem> SuggestedActions,
    bool RequiresHumanReview,
    string ReviewReason
);

public record WorkIntelligenceAnalysisResult(
    string AnalysisId,
    string PropertyId,
    string TenantId,
    GroundedContext Context,
    GroundedDraftResponse Draft,
    DateTimeOffset ProcessedAtUtc
);
