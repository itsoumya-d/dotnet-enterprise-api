namespace OrdersApi.Models;

public enum PropertyRatingStatus
{
    Pending,
    Validated,
    Migrated,
    FailedValidation,
    DuplicateSuppressed
}

public class PropertyRatingRecord
{
    public string AssessmentNumber { get; set; } = string.Empty;
    public string ValuationNumber { get; set; } = string.Empty;
    public string PropertyAddress { get; set; } = string.Empty;
    public string LegalDescription { get; set; } = string.Empty;
    public decimal LandValue { get; set; }
    public decimal CapitalValue { get; set; }
    public string RatingCategory { get; set; } = "Residential"; // Residential, Commercial, Rural, Utilities
    public string TechOneMappingCode { get; set; } = string.Empty;
    public PropertyRatingStatus Status { get; set; } = PropertyRatingStatus.Pending;
    public string? ValidationErrorMessage { get; set; }
}

public class PropertyRatingBatchRequest
{
    public string BatchId { get; set; } = string.Empty;
    public string CouncilIdentifier { get; set; } = "Whanganui District Council";
    public string SourceSystem { get; set; } = "TechnologyOne Property & Rating Integration";
    public List<PropertyRatingRecord> Records { get; set; } = new();
}

public class PropertyRatingBatchResult
{
    public string BatchId { get; set; } = string.Empty;
    public string CouncilIdentifier { get; set; } = string.Empty;
    public int TotalRecords { get; set; }
    public int ValidatedCount { get; set; }
    public int FailedCount { get; set; }
    public string Status { get; set; } = "Completed";
    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
    public List<PropertyRatingRecord> ProcessedRecords { get; set; } = new();
    public List<PropertyValidationError> ValidationErrors { get; set; } = new();
}

public class PropertyValidationError
{
    public string AssessmentNumber { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}
