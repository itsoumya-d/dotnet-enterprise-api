using OrdersApi.Models;

namespace OrdersApi.Services;

public interface IEngineeringPracticeService
{
    PracticeReviewSummary EvaluateChange(CodeChangeReviewRequest request);
    void RecordDeployment(DoraDeploymentEvent deployment);
    DoraMetricsReport GetDoraMetrics(string squad, int windowDays = 30);
    IReadOnlyList<EngineeringStandard> GetStandardsHandbook();
}

public class EngineeringPracticeService : IEngineeringPracticeService
{
    private readonly List<DoraDeploymentEvent> _deployments = new();
    private readonly object _lock = new();

    private static readonly IReadOnlyList<EngineeringStandard> Standards = new List<EngineeringStandard>
    {
        new(
            "MAS-ARCH-001",
            "Clean Architecture Layer Boundaries",
            RuleCategory.Architecture,
            "Presentation and Controller layers must not instantiate database contexts, raw SQL commands, or low-level HTTP sockets directly.",
            "Automated CI Gate (Blocking Error)",
            "MAS Target Architecture Standard 2.4"
        ),
        new(
            "MAS-SEC-002",
            "Zero Hardcoded Credentials & Secrets",
            RuleCategory.Security,
            "No plaintext passwords, API keys, Azure ConnectionStrings, or private keys allowed in committed code or configuration files.",
            "Automated Secret Scan & Pre-commit Hook",
            "RBNZ BS11 / FMA Operational Resilience Guidelines"
        ),
        new(
            "MAS-RESIL-003",
            "Async Cancellation & Circuit Breakers",
            RuleCategory.Resilience,
            "All outbound I/O, database queries, and external HTTP integrations must accept CancellationTokens and declare circuit breakers.",
            "Static Analysis Policy",
            "Cloud Native Architecture Resilience Pattern"
        ),
        new(
            "MAS-TEST-004",
            "Mandatory Test Coverage for Business Logic",
            RuleCategory.Testing,
            "Any change introducing or modifying business logic or domain services must include corresponding automated unit/integration test fixtures.",
            "Branch Protection Rule",
            "MAS Quality Assurance Charter §3.1"
        ),
        new(
            "MAS-AI-005",
            "Safe AI-Assisted Development Guardrail",
            RuleCategory.AiGovernance,
            "AI-assisted pull requests must be accompanied by human developer verification, input sanitization, and explicit boundary regression tests.",
            "Peer Review & Automated LLM Guardrail",
            "MAS Responsible AI Policy 2026"
        )
    };

    public IReadOnlyList<EngineeringStandard> GetStandardsHandbook() => Standards;

    public PracticeReviewSummary EvaluateChange(CodeChangeReviewRequest request)
    {
        var findings = new List<PracticeFinding>();
        int totalAdditions = request.Files.Sum(f => f.Additions);
        bool hasTestFiles = request.Files.Any(f => f.Path.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                                                   f.Path.Contains("spec", StringComparison.OrdinalIgnoreCase));
        bool hasDomainOrServiceChanges = request.Files.Any(f => f.Path.Contains("service", StringComparison.OrdinalIgnoreCase) ||
                                                               f.Path.Contains("model", StringComparison.OrdinalIgnoreCase) ||
                                                               f.Path.Contains("controller", StringComparison.OrdinalIgnoreCase));

        // 1. Mandatory Test Coverage Rule
        if (hasDomainOrServiceChanges && totalAdditions >= 40 && !hasTestFiles)
        {
            findings.Add(new PracticeFinding(
                "MAS-TEST-004",
                RuleCategory.Testing,
                FindingSeverity.Error,
                $"Change introduces {totalAdditions} additions across business services without any corresponding test files.",
                "Add automated unit or integration tests matching the modified services before merging."
            ));
        }

        foreach (var file in request.Files)
        {
            var content = file.PatchContent ?? string.Empty;

            // 2. Secret Scan Rule
            if (content.Contains("password=", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("client_secret=", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("DefaultEndpointsProtocol=https;", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("Server=tcp:", StringComparison.OrdinalIgnoreCase) && content.Contains("Password=", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new PracticeFinding(
                    "MAS-SEC-002",
                    RuleCategory.Security,
                    FindingSeverity.Error,
                    $"Hardcoded credentials or sensitive connection strings detected in '{file.Path}'.",
                    "Store sensitive secrets in Azure Key Vault or local User Secrets and inject via configuration.",
                    file.Path
                ));
            }

            // 3. Architecture Layering Rule
            if (file.Path.Contains("Controller", StringComparison.OrdinalIgnoreCase) &&
                (content.Contains("new DbContext", StringComparison.OrdinalIgnoreCase) ||
                 content.Contains("SqlConnection", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new PracticeFinding(
                    "MAS-ARCH-001",
                    RuleCategory.Architecture,
                    FindingSeverity.Error,
                    $"Direct database instantiation detected inside controller '{file.Path}'.",
                    "Encapsulate data access within scoped repository or domain service interfaces.",
                    file.Path
                ));
            }

            // 4. Resilience Rule: Async CancellationToken (exclude test files)
            if (!file.Path.Contains("test", StringComparison.OrdinalIgnoreCase) &&
                file.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                content.Contains("public async Task", StringComparison.OrdinalIgnoreCase) &&
                !content.Contains("CancellationToken", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new PracticeFinding(
                    "MAS-RESIL-003",
                    RuleCategory.Resilience,
                    FindingSeverity.Warning,
                    $"Async method in '{file.Path}' does not accept a CancellationToken.",
                    "Pass CancellationToken ct = default to allow graceful timeout and cancellation propagation.",
                    file.Path
                ));
            }

            // 5. AI-Assisted Guardrail
            if (request.HasAiAssistance)
            {
                if (content.Contains("eval(", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("BinaryFormatter", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("JavaScriptSerializer", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new PracticeFinding(
                        "MAS-AI-005",
                        RuleCategory.AiGovernance,
                        FindingSeverity.Error,
                        $"High-risk insecure deserialization pattern generated in AI-assisted change '{file.Path}'.",
                        "Replace with System.Text.Json using strongly typed serialization models.",
                        file.Path
                    ));
                }

                if (request.IsRegulatedFinancialSurface && !content.Contains("Audit", StringComparison.OrdinalIgnoreCase) &&
                    !content.Contains("Telemetry", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new PracticeFinding(
                        "MAS-AI-005",
                        RuleCategory.AiGovernance,
                        FindingSeverity.Warning,
                        $"AI-assisted modification on regulated financial surface '{file.Path}' lacks explicit audit logging or telemetry markers.",
                        "Verify audit trail instrumentation is present for auditability per MAS compliance standards.",
                        file.Path
                    ));
                }
            }
        }

        int errors = findings.Count(f => f.Severity == FindingSeverity.Error);
        int warnings = findings.Count(f => f.Severity == FindingSeverity.Warning);
        bool approved = errors == 0;
        int qualityScore = Math.Max(0, 100 - (errors * 25) - (warnings * 5));

        return new PracticeReviewSummary(
            request.PullRequestId,
            request.Squad,
            approved,
            qualityScore,
            errors,
            warnings,
            findings,
            DateTime.UtcNow
        );
    }

    public void RecordDeployment(DoraDeploymentEvent deployment)
    {
        lock (_lock)
        {
            _deployments.Add(deployment);
        }
    }

    public DoraMetricsReport GetDoraMetrics(string squad, int windowDays = 30)
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow.AddDays(-windowDays);
            var squadDeployments = _deployments
                .Where(d => d.Squad.Equals(squad, StringComparison.OrdinalIgnoreCase) && d.DeployedAtUtc >= cutoff)
                .ToList();

            if (squadDeployments.Count == 0)
            {
                return new DoraMetricsReport(
                    squad,
                    0,
                    0.0,
                    0.0,
                    0.0,
                    "Low (No recent telemetry)",
                    DateTime.UtcNow
                );
            }

            double weeks = Math.Max(1.0, windowDays / 7.0);
            double freqPerWeek = Math.Round(squadDeployments.Count / weeks, 2);

            var sortedLeadTimes = squadDeployments.Select(d => d.LeadTimeMinutes).OrderBy(t => t).ToList();
            double medianMinutes = sortedLeadTimes[sortedLeadTimes.Count / 2];
            double medianHours = Math.Round(medianMinutes / 60.0, 2);

            int failures = squadDeployments.Count(d => d.IsChangeFailure);
            double failureRate = Math.Round((double)failures / squadDeployments.Count * 100.0, 1);

            string tier = (freqPerWeek >= 7.0 && medianHours <= 24.0 && failureRate <= 5.0) ? "Elite" :
                          (freqPerWeek >= 2.0 && medianHours <= 72.0 && failureRate <= 15.0) ? "High" :
                          (freqPerWeek >= 0.5 && failureRate <= 25.0) ? "Medium" : "Low";

            return new DoraMetricsReport(
                squad,
                squadDeployments.Count,
                freqPerWeek,
                medianHours,
                failureRate,
                tier,
                DateTime.UtcNow
            );
        }
    }
}
