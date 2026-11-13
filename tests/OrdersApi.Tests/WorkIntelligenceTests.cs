using System.Net;
using System.Net.Http.Json;
using OrdersApi.Models;
using Xunit;

namespace OrdersApi.Tests;

public class WorkIntelligenceTests : IClassFixture<IntegrationApiFactory>
{
    private readonly HttpClient _client;

    public WorkIntelligenceTests(IntegrationApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AnalyzeTenantCommunication_MaintenanceHvac_ReturnsGroundedLeaseClauseAndDraft()
    {
        var request = new TenantCommunicationRequest(
            PropertyId: "PROP-AKL-COMM-101",
            TenantId: "TENANT-APEX-LOGISTICS",
            SenderEmail: "facilities@apexlogistics.co.nz",
            Subject: "Urgent: Server room air conditioning unit failure",
            MessageBody: "Our HVAC cooling system in unit 301 is blowing ambient air and compressor seems faulty. Please send technician under landlord maintenance."
        );

        var response = await _client.PostAsJsonAsync("/api/work-intelligence/analyze", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<WorkIntelligenceAnalysisResult>();
        Assert.NotNull(result);
        Assert.Equal("PROP-AKL-COMM-101", result.PropertyId);
        Assert.Equal("TENANT-APEX-LOGISTICS", result.TenantId);
        Assert.NotEmpty(result.Context.MatchedClauses);
        Assert.Contains(result.Context.MatchedClauses, c => c.ClauseId == "CL-MAINT-HVAC-12.4");
        Assert.Contains("Section 12.4", result.Draft.CitedSections);
        Assert.Contains("Section 12.4", result.Draft.DraftBody);
        Assert.False(result.Draft.RequiresHumanReview);
        Assert.Equal("Low", result.Context.RiskTier);
        Assert.True(result.Context.GroundingConfidence >= 0.85);
    }

    [Fact]
    public async Task AnalyzeTenantCommunication_OpexReconciliationInquiry_CitesOpexAuditClause()
    {
        var request = new TenantCommunicationRequest(
            PropertyId: "PROP-AKL-COMM-101",
            TenantId: "TENANT-VALENTINE-LAW",
            SenderEmail: "partner@valentinelaw.co.nz",
            Subject: "Query regarding FY2026 OPEX CAM outgoings reconciliation",
            MessageBody: "We received the annual operating expenses statement. Under our lease terms, we request the line-item audit breakdown for common area energy charges."
        );

        var response = await _client.PostAsJsonAsync("/api/work-intelligence/analyze", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<WorkIntelligenceAnalysisResult>();
        Assert.NotNull(result);
        Assert.Contains(result.Context.MatchedClauses, c => c.ClauseId == "CL-OPEX-AUDIT-08.2");
        Assert.Contains("Section 8.2", result.Draft.CitedSections);
        Assert.Contains(result.Draft.SuggestedActions, a => a.ActionType == "PullOpexLedger");
    }

    [Fact]
    public async Task AnalyzeTenantCommunication_RentReviewDispute_FlagsHighRiskAndRequiresHumanReview()
    {
        var request = new TenantCommunicationRequest(
            PropertyId: "PROP-AKL-COMM-101",
            TenantId: "TENANT-GLOBAL-RETAIL",
            SenderEmail: "legal@globalretail.co.nz",
            Subject: "Formal Notice: Rent Review Dispute and Arbitration Request",
            MessageBody: "We formally dispute the proposed market rent increase and our lawyer has instructed us to initiate arbitration for alleged breach of covenant."
        );

        var response = await _client.PostAsJsonAsync("/api/work-intelligence/analyze", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<WorkIntelligenceAnalysisResult>();
        Assert.NotNull(result);
        Assert.Equal("High", result.Context.RiskTier);
        Assert.True(result.Draft.RequiresHumanReview);
        Assert.Contains("arbitration", result.Draft.ReviewReason.ToLowerInvariant());
        Assert.Contains(result.Draft.SuggestedActions, a => a.ActionType == "LegalConsultation");
    }

    [Fact]
    public async Task AnalyzeTenantCommunication_EmptyBody_ReturnsBadRequest()
    {
        var request = new TenantCommunicationRequest(
            PropertyId: "PROP-AKL-COMM-101",
            TenantId: "TENANT-TEST",
            SenderEmail: "test@test.co.nz",
            Subject: "Empty message",
            MessageBody: "   "
        );

        var response = await _client.PostAsJsonAsync("/api/work-intelligence/analyze", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetLeaseClauses_ReturnsStandardCommercialClausesForProperty()
    {
        var response = await _client.GetAsync("/api/work-intelligence/clauses/commercial-default");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var clauses = await response.Content.ReadFromJsonAsync<List<LeaseClause>>();
        Assert.NotNull(clauses);
        Assert.True(clauses.Count >= 4);
        Assert.Contains(clauses, c => c.Category == "Maintenance");
        Assert.Contains(clauses, c => c.Category == "OPEX");
        Assert.Contains(clauses, c => c.Category == "RentReview");
    }
}
