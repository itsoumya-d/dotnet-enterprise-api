using System.Net;
using System.Net.Http.Json;
using OrdersApi.Models;
using OrdersApi.Services;
using Xunit;

namespace OrdersApi.Tests;

public class EngineeringPracticeGovernanceTests : IClassFixture<IntegrationApiFactory>
{
    private readonly HttpClient _client;
    private readonly EngineeringPracticeService _unitService;

    public EngineeringPracticeGovernanceTests(IntegrationApiFactory factory)
    {
        _client = factory.CreateClient();
        _unitService = new EngineeringPracticeService();
    }

    [Fact]
    public void EvaluateChange_CleanChangeWithTests_ReturnsApprovedAndHighScore()
    {
        var request = new CodeChangeReviewRequest(
            PullRequestId: "PR-101",
            Squad: "Core-Insurance-Squad",
            Author: "developer@mas.co.nz",
            HasAiAssistance: false,
            IsRegulatedFinancialSurface: true,
            Files: new List<CodeChangeFile>
            {
                new("src/OrdersApi/Services/ClaimsRatingService.cs", 60, 5,
                    "public async Task<RatingResult> ComputeRatingAsync(PolicyRequest req, CancellationToken ct = default) { AuditLogger.Log(\"Rating computed\"); return await _ratingEngine.ExecuteAsync(req, ct); }"),
                new("tests/OrdersApi.Tests/ClaimsRatingTests.cs", 80, 2,
                    "[Fact] public async Task ComputeRating_ValidRequest_ReturnsExpectedPremium() { Assert.True(true); }")
            }
        );

        var result = _unitService.EvaluateChange(request);

        Assert.True(result.IsApproved);
        Assert.Equal(0, result.ErrorCount);
        Assert.Equal(100, result.QualityScore);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void EvaluateChange_MissingTestsOnSubstantialDomainChange_TriggersBlockingError()
    {
        var request = new CodeChangeReviewRequest(
            PullRequestId: "PR-102",
            Squad: "Wealth-Advice-Squad",
            Author: "developer@mas.co.nz",
            HasAiAssistance: false,
            IsRegulatedFinancialSurface: false,
            Files: new List<CodeChangeFile>
            {
                new("src/OrdersApi/Services/KiwiSaverAllocationService.cs", 85, 10,
                    "public void RebalancePortfolio() { /* substantial business logic */ }")
            }
        );

        var result = _unitService.EvaluateChange(request);

        Assert.False(result.IsApproved);
        Assert.True(result.ErrorCount >= 1);
        Assert.Contains(result.Findings, f => f.RuleId == "MAS-TEST-004" && f.Severity == FindingSeverity.Error);
    }

    [Fact]
    public void EvaluateChange_HardcodedSecrets_TriggersSecurityBlockingError()
    {
        var request = new CodeChangeReviewRequest(
            PullRequestId: "PR-103",
            Squad: "Platform-Foundations",
            Author: "contractor@mas.co.nz",
            HasAiAssistance: false,
            IsRegulatedFinancialSurface: true,
            Files: new List<CodeChangeFile>
            {
                new("src/OrdersApi/appsettings.json", 15, 0,
                    "\"AzureServiceBus\": \"Endpoint=sb://mas-bus.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=DefaultEndpointsProtocol=https;password=SuperSecret123;\""),
                new("tests/OrdersApi.Tests/ConfigTests.cs", 20, 0, "[Fact] public void Test() {}")
            }
        );

        var result = _unitService.EvaluateChange(request);

        Assert.False(result.IsApproved);
        Assert.Contains(result.Findings, f => f.RuleId == "MAS-SEC-002" && f.Severity == FindingSeverity.Error);
    }

    [Fact]
    public void EvaluateChange_DirectDbInController_TriggersArchitectureViolation()
    {
        var request = new CodeChangeReviewRequest(
            PullRequestId: "PR-104",
            Squad: "Member-Portal-Squad",
            Author: "developer@mas.co.nz",
            HasAiAssistance: false,
            IsRegulatedFinancialSurface: false,
            Files: new List<CodeChangeFile>
            {
                new("src/OrdersApi/Controllers/MemberPolicyController.cs", 30, 0,
                    "var context = new DbContext(); return Ok(context.Policies.ToList());"),
                new("tests/OrdersApi.Tests/MemberPolicyTests.cs", 25, 0, "[Fact] public void Test() {}")
            }
        );

        var result = _unitService.EvaluateChange(request);

        Assert.False(result.IsApproved);
        Assert.Contains(result.Findings, f => f.RuleId == "MAS-ARCH-001");
    }

    [Fact]
    public void EvaluateChange_AiAssistedWithInsecureDeserialization_TriggersAiGuardrailError()
    {
        var request = new CodeChangeReviewRequest(
            PullRequestId: "PR-105",
            Squad: "Innovation-Lab",
            Author: "ai-bot@mas.co.nz",
            HasAiAssistance: true,
            IsRegulatedFinancialSurface: true,
            Files: new List<CodeChangeFile>
            {
                new("src/OrdersApi/Services/AiPayloadParser.cs", 45, 0,
                    "var formatter = new BinaryFormatter(); return formatter.Deserialize(stream);"),
                new("tests/OrdersApi.Tests/AiPayloadTests.cs", 30, 0, "[Fact] public void Test() {}")
            }
        );

        var result = _unitService.EvaluateChange(request);

        Assert.False(result.IsApproved);
        Assert.Contains(result.Findings, f => f.RuleId == "MAS-AI-005" && f.Severity == FindingSeverity.Error);
    }

    [Fact]
    public void DoraMetrics_RecordsDeploymentsAndComputesCorrectTiers()
    {
        var service = new EngineeringPracticeService();
        var now = DateTime.UtcNow;

        // Record 14 fast, successful deployments over 7 days for 'Elite-Squad' (2/day)
        for (int i = 0; i < 14; i++)
        {
            service.RecordDeployment(new DoraDeploymentEvent(
                DeploymentId: $"DEP-ELITE-{i}",
                Squad: "Elite-Squad",
                ServiceName: "PolicyUnderwritingApi",
                Environment: "Production",
                CommitSha: $"sha-elite-{i}",
                LeadTimeMinutes: 45.0, // 45 mins lead time
                IsChangeFailure: false,
                DeployedAtUtc: now.AddHours(-i * 8)
            ));
        }

        var report = service.GetDoraMetrics("Elite-Squad", windowDays: 7);

        Assert.Equal("Elite-Squad", report.Squad);
        Assert.Equal(14, report.TotalDeployments);
        Assert.Equal(0.0, report.ChangeFailureRatePercent);
        Assert.Equal(0.75, report.MedianLeadTimeHours); // 45 / 60 = 0.75 hrs
        Assert.Equal("Elite", report.PerformanceTier);
    }

    [Fact]
    public async Task ApiEndpoints_ReviewAndStandardsHandbook_ReturnOk()
    {
        // 1. Get Standards Handbook
        var handbookResponse = await _client.GetAsync("/api/practice/governance/standards-handbook");
        Assert.Equal(HttpStatusCode.OK, handbookResponse.StatusCode);

        var standards = await handbookResponse.Content.ReadFromJsonAsync<List<EngineeringStandard>>();
        Assert.NotNull(standards);
        Assert.True(standards.Count >= 5);
        Assert.Contains(standards, s => s.Id == "MAS-ARCH-001");
        Assert.Contains(standards, s => s.Id == "MAS-AI-005");

        // 2. Post Review Change via HTTP
        var reviewRequest = new CodeChangeReviewRequest(
            PullRequestId: "PR-HTTP-201",
            Squad: "Claims-Digital-Squad",
            Author: "lead@mas.co.nz",
            HasAiAssistance: true,
            IsRegulatedFinancialSurface: true,
            Files: new List<CodeChangeFile>
            {
                new("src/OrdersApi/Services/ClaimsIntakeService.cs", 50, 0,
                    "public async Task ProcessClaim(Claim claim, CancellationToken ct = default) { Telemetry.Track(\"ClaimProcessed\"); }"),
                new("tests/OrdersApi.Tests/ClaimsIntakeTests.cs", 40, 0,
                    "[Fact] public void TestIntake() { Assert.True(true); }")
            }
        );

        var reviewResponse = await _client.PostAsJsonAsync("/api/practice/governance/review-change", reviewRequest);
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);

        var summary = await reviewResponse.Content.ReadFromJsonAsync<PracticeReviewSummary>();
        Assert.NotNull(summary);
        Assert.True(summary.IsApproved);
        Assert.Equal("PR-HTTP-201", summary.PullRequestId);

        // 3. Post Deployment Event
        var depEvent = new DoraDeploymentEvent(
            DeploymentId: "DEP-HTTP-999",
            Squad: "Claims-Digital-Squad",
            ServiceName: "ClaimsPortal",
            Environment: "Production",
            CommitSha: "abc1234",
            LeadTimeMinutes: 120.0,
            IsChangeFailure: false,
            DeployedAtUtc: DateTime.UtcNow
        );

        var depResponse = await _client.PostAsJsonAsync("/api/practice/governance/record-deployment", depEvent);
        Assert.Equal(HttpStatusCode.Accepted, depResponse.StatusCode);

        // 4. Get DORA Metrics
        var doraResponse = await _client.GetAsync("/api/practice/governance/dora-metrics/Claims-Digital-Squad");
        Assert.Equal(HttpStatusCode.OK, doraResponse.StatusCode);

        var doraReport = await doraResponse.Content.ReadFromJsonAsync<DoraMetricsReport>();
        Assert.NotNull(doraReport);
        Assert.Equal("Claims-Digital-Squad", doraReport.Squad);
        Assert.True(doraReport.TotalDeployments >= 1);
    }
}
