using System.Net;
using System.Net.Http.Json;
using OrdersApi.Models;
using OrdersApi.Services;
using Xunit;

namespace OrdersApi.Tests;

public class PropertyRatingIntegrationTests : IDisposable
{
    private readonly IntegrationApiFactory _factory = new();
    private readonly HttpClient _client;
    private readonly PropertyRatingMigrationService _unitService = new();

    public PropertyRatingIntegrationTests()
    {
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-API-Key", "dev-integration-key");
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task IngestBatch_WithoutApiKey_ReturnsUnauthorized()
    {
        using var anonymousClient = _factory.CreateClient();
        var request = new PropertyRatingBatchRequest
        {
            BatchId = "batch-anon-1",
            Records = new List<PropertyRatingRecord>
            {
                new()
                {
                    AssessmentNumber = "12345",
                    ValuationNumber = "12345/678.01",
                    PropertyAddress = "100 Victoria Avenue, Whanganui",
                    LegalDescription = "Lot 1 DP 12345",
                    LandValue = 250000m,
                    CapitalValue = 650000m,
                    RatingCategory = "Commercial"
                }
            }
        };

        var response = await anonymousClient.PostAsJsonAsync("/api/integrations/property-rating/batch", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IngestBatch_EmptyRecords_ReturnsBadRequest()
    {
        var request = new PropertyRatingBatchRequest
        {
            BatchId = "batch-empty",
            Records = new List<PropertyRatingRecord>()
        };

        var response = await _client.PostAsJsonAsync("/api/integrations/property-rating/batch", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task IngestBatch_ValidWhanganuiRecords_SucceedsAndMapsTechnologyOneCodes()
    {
        var request = new PropertyRatingBatchRequest
        {
            BatchId = "batch-wanganui-2026-001",
            CouncilIdentifier = "Whanganui District Council",
            SourceSystem = "TechnologyOne Property & Rating",
            Records = new List<PropertyRatingRecord>
            {
                new()
                {
                    AssessmentNumber = "A-10029",
                    ValuationNumber = "13420/512.01",
                    PropertyAddress = "101 Guyton Street, Whanganui",
                    LegalDescription = "Sec 45 Town of Whanganui",
                    LandValue = 320000m,
                    CapitalValue = 890000m,
                    RatingCategory = "Commercial"
                },
                new()
                {
                    AssessmentNumber = "A-10030",
                    ValuationNumber = "13420/513.00",
                    PropertyAddress = "42 Somme Parade, Whanganui",
                    LegalDescription = "Lot 8 DP 4567",
                    LandValue = 210000m,
                    CapitalValue = 540000m,
                    RatingCategory = "Residential"
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/integrations/property-rating/batch", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PropertyRatingBatchResult>();
        Assert.NotNull(result);
        Assert.Equal("Success", result.Status);
        Assert.Equal(2, result.TotalRecords);
        Assert.Equal(2, result.ValidatedCount);
        Assert.Equal(0, result.FailedCount);

        var firstRecord = result.ProcessedRecords.First(r => r.AssessmentNumber == "A-10029");
        Assert.Equal(PropertyRatingStatus.Validated, firstRecord.Status);
        Assert.Equal("T1-PR-COMMERCIAL-1342051201", firstRecord.TechOneMappingCode);

        // Verify status retrieval via GET endpoint
        var getResponse = await _client.GetAsync($"/api/integrations/property-rating/batch/{result.BatchId}");
        getResponse.EnsureSuccessStatusCode();
        var retrieved = await getResponse.Content.ReadFromJsonAsync<PropertyRatingBatchResult>();
        Assert.NotNull(retrieved);
        Assert.Equal(result.BatchId, retrieved.BatchId);
    }

    [Fact]
    public async Task IngestBatch_InvalidValuationAndNegativeLand_ReportsValidationErrors()
    {
        var request = new PropertyRatingBatchRequest
        {
            BatchId = "batch-invalid-test",
            Records = new List<PropertyRatingRecord>
            {
                new()
                {
                    AssessmentNumber = "A-ERR-01",
                    ValuationNumber = "INVALID-VALUATION-FORMAT",
                    PropertyAddress = "15 Ridgway Street, Whanganui",
                    LandValue = -5000m,
                    CapitalValue = 400000m,
                    RatingCategory = "Commercial"
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/integrations/property-rating/batch", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PropertyRatingBatchResult>();
        Assert.NotNull(result);
        Assert.Equal("Failed", result.Status);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(0, result.ValidatedCount);
        Assert.Contains(result.ValidationErrors, e => e.FieldName == "ValuationNumber");
        Assert.Contains(result.ValidationErrors, e => e.FieldName == "LandValue");
    }

    [Fact]
    public async Task IngestBatch_DuplicateAssessmentNumber_FlagsDuplicateError()
    {
        var request = new PropertyRatingBatchRequest
        {
            BatchId = "batch-dupe-test",
            Records = new List<PropertyRatingRecord>
            {
                new()
                {
                    AssessmentNumber = "A-DUP-99",
                    ValuationNumber = "13420/100.00",
                    PropertyAddress = "12 Saint Hill Street, Whanganui",
                    LandValue = 150000m,
                    CapitalValue = 450000m,
                    RatingCategory = "Residential"
                },
                new()
                {
                    AssessmentNumber = "A-DUP-99", // duplicate assessment
                    ValuationNumber = "13420/101.00",
                    PropertyAddress = "14 Saint Hill Street, Whanganui",
                    LandValue = 160000m,
                    CapitalValue = 460000m,
                    RatingCategory = "Residential"
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/integrations/property-rating/batch", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PropertyRatingBatchResult>();
        Assert.NotNull(result);
        Assert.Equal("PartialSuccess", result.Status);
        Assert.Equal(1, result.ValidatedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Contains(result.ValidationErrors, e => e.ErrorMessage.Contains("Duplicate assessment number"));
    }

    [Fact]
    public async Task IngestBatch_LandValueExceedsCapitalValue_FlagsValidationError()
    {
        var request = new PropertyRatingBatchRequest
        {
            BatchId = "batch-land-excess",
            Records = new List<PropertyRatingRecord>
            {
                new()
                {
                    AssessmentNumber = "A-EXCESS-01",
                    ValuationNumber = "13420/200.00",
                    PropertyAddress = "50 Taupo Quay, Whanganui",
                    LandValue = 900000m,
                    CapitalValue = 600000m, // Land value > Capital value is invalid
                    RatingCategory = "Commercial"
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/integrations/property-rating/batch", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PropertyRatingBatchResult>();
        Assert.NotNull(result);
        Assert.Equal(1, result.FailedCount);
        Assert.Contains(result.ValidationErrors, e => e.ErrorMessage.Contains("cannot exceed total capital valuation"));
    }

    [Fact]
    public async Task GetBatchStatus_NonExistentBatch_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/integrations/property-rating/batch/non-existent-batch-999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
