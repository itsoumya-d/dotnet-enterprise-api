using OrdersApi.Models;

namespace OrdersApi.Services;

public interface IWorkIntelligenceService
{
    Task<WorkIntelligenceAnalysisResult> AnalyzeCommunicationAsync(TenantCommunicationRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LeaseClause>> GetLeaseClausesAsync(string propertyId, CancellationToken cancellationToken = default);
}

public class WorkIntelligenceService : IWorkIntelligenceService
{
    private static readonly Dictionary<string, List<LeaseClause>> SampleClauses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["commercial-default"] = new List<LeaseClause>
        {
            new(
                ClauseId: "CL-MAINT-HVAC-12.4",
                SectionNumber: "Section 12.4",
                Category: "Maintenance",
                Title: "Air Conditioning & HVAC Service Maintenance",
                Summary: "Landlord responsible for structural plant & capital replacement; Tenant responsible for quarterly preventive maintenance servicing.",
                VerbatimExcerpt: "The Tenant shall maintain a comprehensive quarterly servicing contract with an approved HVAC engineer. Capital replacements shall remain the Landlord's sole responsibility.",
                IsLandlordLiability: false,
                RequiresFormalNotice: false
            ),
            new(
                ClauseId: "CL-OPEX-AUDIT-08.2",
                SectionNumber: "Section 8.2",
                Category: "OPEX",
                Title: "Operating Expense Reconciliation & Tenant Audit Rights",
                Summary: "Annual CAM/OPEX statement provided within 90 days of fiscal year-end; tenant holds 30-day objection right before disputed proportion is payable.",
                VerbatimExcerpt: "The Landlord shall deliver the final Operating Expenses certificate within ninety (90) days. Objections must be lodged within thirty (30) days in writing.",
                IsLandlordLiability: false,
                RequiresFormalNotice: true
            ),
            new(
                ClauseId: "CL-RENT-REVIEW-04.1",
                SectionNumber: "Section 4.1",
                Category: "RentReview",
                Title: "Market Rent Review & Arbitration Procedure",
                Summary: "Market rent review subject to registered valuer appointment if parties fail to agree within 20 working days.",
                VerbatimExcerpt: "If the parties are unable to reach agreement within twenty (20) working days of the Landlord's notice, the current market rent shall be determined by arbitration.",
                IsLandlordLiability: false,
                RequiresFormalNotice: true
            ),
            new(
                ClauseId: "CL-INSUR-CASUALTY-15.3",
                SectionNumber: "Section 15.3",
                Category: "Insurance",
                Title: "Casualty Damage & Rent Abatement",
                Summary: "Rent abates in fair proportion to tenant's inability to occupy premises following insured casualty damage.",
                VerbatimExcerpt: "If the Premises are damaged by fire, flood or other insured risk so as to be rendered untenantable, the Rent shall abate proportionally.",
                IsLandlordLiability: true,
                RequiresFormalNotice: false
            )
        }
    };

    public Task<IReadOnlyList<LeaseClause>> GetLeaseClausesAsync(string propertyId, CancellationToken cancellationToken = default)
    {
        if (SampleClauses.TryGetValue(propertyId, out var clauses))
        {
            return Task.FromResult<IReadOnlyList<LeaseClause>>(clauses);
        }
        return Task.FromResult<IReadOnlyList<LeaseClause>>(SampleClauses["commercial-default"]);
    }

    public async Task<WorkIntelligenceAnalysisResult> AnalyzeCommunicationAsync(TenantCommunicationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.MessageBody))
        {
            throw new ArgumentException("Message body cannot be empty.", nameof(request));
        }

        var clauses = await GetLeaseClausesAsync(request.PropertyId, cancellationToken);
        var combinedText = $"{request.Subject} {request.MessageBody}".ToLowerInvariant();

        var matchedClauses = new List<LeaseClause>();
        var citedSections = new List<string>();
        var suggestedActions = new List<DraftActionItem>();
        var precedents = new List<string>();

        string riskTier = "Low";
        bool requiresHumanReview = false;
        string reviewReason = "Standard routine inquiry grounded by contract clauses.";
        double confidence = 0.92;

        if (combinedText.Contains("hvac") || combinedText.Contains("air conditioning") || combinedText.Contains("cooling") || combinedText.Contains("heating"))
        {
            var hvacClause = clauses.FirstOrDefault(c => c.Category == "Maintenance");
            if (hvacClause != null)
            {
                matchedClauses.Add(hvacClause);
                citedSections.Add(hvacClause.SectionNumber);
                suggestedActions.Add(new DraftActionItem("VerifyContractorLog", "Request tenant's quarterly HVAC maintenance log per Section 12.4", "PropertyManager", "2"));
                suggestedActions.Add(new DraftActionItem("DispatchTechnician", "Assign preferred mechanical contractor for primary inspection", "Contractor", "1"));
                precedents.Add("Building B unit 402 compressor repair resolved under Section 12.4 in Q2 2026.");
            }
        }

        if (combinedText.Contains("opex") || combinedText.Contains("outgoings") || combinedText.Contains("reconciliation") || combinedText.Contains("cam"))
        {
            var opexClause = clauses.FirstOrDefault(c => c.Category == "OPEX");
            if (opexClause != null)
            {
                matchedClauses.Add(opexClause);
                citedSections.Add(opexClause.SectionNumber);
                suggestedActions.Add(new DraftActionItem("PullOpexLedger", "Retrieve YTD audited operating expenditure ledger from accounting core", "Finance", "3"));
                precedents.Add("FY2025 OPEX reconciliation audited by BDO completed within statutory 90-day window.");
            }
        }

        if (combinedText.Contains("dispute") || combinedText.Contains("lawyer") || combinedText.Contains("arbitration") || combinedText.Contains("breach") || combinedText.Contains("terminate"))
        {
            var rentReviewClause = clauses.FirstOrDefault(c => c.Category == "RentReview");
            if (rentReviewClause != null)
            {
                matchedClauses.Add(rentReviewClause);
                citedSections.Add(rentReviewClause.SectionNumber);
            }
            riskTier = "High";
            requiresHumanReview = true;
            confidence = 0.78;
            reviewReason = "Escalated terms detected (dispute/arbitration). Mandates senior asset manager sign-off before dispatch.";
            suggestedActions.Add(new DraftActionItem("LegalConsultation", "Brief legal counsel on notice timeline per Section 4.1", "AssetManager", "1"));
        }

        if (matchedClauses.Count == 0)
        {
            // Default general clause
            var defaultClause = clauses.First();
            matchedClauses.Add(defaultClause);
            citedSections.Add(defaultClause.SectionNumber);
            confidence = 0.80;
            requiresHumanReview = true;
            reviewReason = "No high-confidence topic match found. Requires operator verification.";
        }

        string draftBody = GenerateGroundedDraft(request, matchedClauses, citedSections, riskTier);

        var context = new GroundedContext(
            MatchedClauses: matchedClauses,
            GroundingConfidence: confidence,
            RiskTier: riskTier,
            PrecedentDecisions: precedents
        );

        var draft = new GroundedDraftResponse(
            DraftBody: draftBody,
            CitedSections: citedSections,
            SuggestedActions: suggestedActions,
            RequiresHumanReview: requiresHumanReview,
            ReviewReason: reviewReason
        );

        return new WorkIntelligenceAnalysisResult(
            AnalysisId: $"WI-{Guid.NewGuid():N}"[..12],
            PropertyId: request.PropertyId,
            TenantId: request.TenantId,
            Context: context,
            Draft: draft,
            ProcessedAtUtc: DateTimeOffset.UtcNow
        );
    }

    private static string GenerateGroundedDraft(TenantCommunicationRequest request, List<LeaseClause> clauses, List<string> sections, string riskTier)
    {
        string sectionCitations = string.Join(" and ", sections);
        return $@"Kia ora,

Thank you for your inquiry regarding {request.Subject}.

We have reviewed your inquiry against your active lease agreement for {request.PropertyId}, specifically {sectionCitations}.

Under {sectionCitations}, {clauses[0].Summary} 

Our operations squad has logged your request and scheduled the corresponding follow-up actions. We will provide a formal update within two working days.

Ngā mihi,
Commercial Property Operations Squad";
    }
}
