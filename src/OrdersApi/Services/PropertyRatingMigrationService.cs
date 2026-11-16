using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using OrdersApi.Models;

namespace OrdersApi.Services;

public interface IPropertyRatingMigrationService
{
    Task<PropertyRatingBatchResult> ProcessMigrationBatchAsync(PropertyRatingBatchRequest request, CancellationToken cancellationToken = default);
    Task<PropertyRatingBatchResult?> GetBatchStatusAsync(string batchId, CancellationToken cancellationToken = default);
}

public class PropertyRatingMigrationService : IPropertyRatingMigrationService
{
    private static readonly ConcurrentDictionary<string, PropertyRatingBatchResult> Batches = new();
    private static readonly HashSet<string> ValidCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Residential", "Commercial", "Industrial", "Rural", "Utilities", "Community"
    };

    private static readonly Regex ValuationRegex = new(@"^[0-9]{3,7}[\/\-]?[0-9]{1,5}(\.[0-9]{1,3})?$", RegexOptions.Compiled);

    public Task<PropertyRatingBatchResult> ProcessMigrationBatchAsync(PropertyRatingBatchRequest request, CancellationToken cancellationToken = default)
    {
        var result = new PropertyRatingBatchResult
        {
            BatchId = string.IsNullOrWhiteSpace(request.BatchId) ? Guid.NewGuid().ToString("N") : request.BatchId,
            CouncilIdentifier = request.CouncilIdentifier,
            TotalRecords = request.Records.Count,
            ProcessedAtUtc = DateTime.UtcNow
        };

        var seenAssessments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in request.Records)
        {
            var errors = ValidateRecord(record, seenAssessments);
            if (errors.Count > 0)
            {
                record.Status = PropertyRatingStatus.FailedValidation;
                record.ValidationErrorMessage = string.Join("; ", errors.Select(e => e.ErrorMessage));
                result.ValidationErrors.AddRange(errors);
                result.FailedCount++;
            }
            else
            {
                record.Status = PropertyRatingStatus.Validated;
                if (string.IsNullOrWhiteSpace(record.TechOneMappingCode))
                {
                    var cleanVal = record.ValuationNumber.Replace("/", "").Replace("-", "").Replace(".", "");
                    record.TechOneMappingCode = $"T1-PR-{record.RatingCategory.ToUpperInvariant()}-{cleanVal}";
                }
                result.ValidatedCount++;
                seenAssessments.Add(record.AssessmentNumber);
            }
            result.ProcessedRecords.Add(record);
        }

        result.Status = result.FailedCount == 0 ? "Success" : (result.ValidatedCount > 0 ? "PartialSuccess" : "Failed");
        Batches[result.BatchId] = result;

        return Task.FromResult(result);
    }

    public Task<PropertyRatingBatchResult?> GetBatchStatusAsync(string batchId, CancellationToken cancellationToken = default)
    {
        Batches.TryGetValue(batchId, out var result);
        return Task.FromResult(result);
    }

    private static List<PropertyValidationError> ValidateRecord(PropertyRatingRecord record, HashSet<string> seenAssessments)
    {
        var errors = new List<PropertyValidationError>();

        if (string.IsNullOrWhiteSpace(record.AssessmentNumber))
        {
            errors.Add(new PropertyValidationError
            {
                AssessmentNumber = record.AssessmentNumber,
                FieldName = nameof(record.AssessmentNumber),
                ErrorMessage = "Assessment number is required."
            });
        }
        else if (seenAssessments.Contains(record.AssessmentNumber))
        {
            errors.Add(new PropertyValidationError
            {
                AssessmentNumber = record.AssessmentNumber,
                FieldName = nameof(record.AssessmentNumber),
                ErrorMessage = $"Duplicate assessment number '{record.AssessmentNumber}' detected in batch."
            });
        }

        if (string.IsNullOrWhiteSpace(record.ValuationNumber))
        {
            errors.Add(new PropertyValidationError
            {
                AssessmentNumber = record.AssessmentNumber,
                FieldName = nameof(record.ValuationNumber),
                ErrorMessage = "Valuation number is required."
            });
        }
        else if (!ValuationRegex.IsMatch(record.ValuationNumber.Trim()))
        {
            errors.Add(new PropertyValidationError
            {
                AssessmentNumber = record.AssessmentNumber,
                FieldName = nameof(record.ValuationNumber),
                ErrorMessage = $"Valuation number '{record.ValuationNumber}' does not match standard New Zealand valuation rolls format."
            });
        }

        if (string.IsNullOrWhiteSpace(record.PropertyAddress))
        {
            errors.Add(new PropertyValidationError
            {
                AssessmentNumber = record.AssessmentNumber,
                FieldName = nameof(record.PropertyAddress),
                ErrorMessage = "Property physical address is required."
            });
        }

        if (record.CapitalValue <= 0)
        {
            errors.Add(new PropertyValidationError
            {
                AssessmentNumber = record.AssessmentNumber,
                FieldName = nameof(record.CapitalValue),
                ErrorMessage = "Capital value must be strictly positive."
            });
        }

        if (record.LandValue < 0)
        {
            errors.Add(new PropertyValidationError
            {
                AssessmentNumber = record.AssessmentNumber,
                FieldName = nameof(record.LandValue),
                ErrorMessage = "Land value cannot be negative."
            });
        }

        if (record.LandValue > record.CapitalValue)
        {
            errors.Add(new PropertyValidationError
            {
                AssessmentNumber = record.AssessmentNumber,
                FieldName = nameof(record.LandValue),
                ErrorMessage = "Land value cannot exceed total capital valuation."
            });
        }

        if (!ValidCategories.Contains(record.RatingCategory))
        {
            errors.Add(new PropertyValidationError
            {
                AssessmentNumber = record.AssessmentNumber,
                FieldName = nameof(record.RatingCategory),
                ErrorMessage = $"Rating category '{record.RatingCategory}' is invalid. Allowed categories: {string.Join(", ", ValidCategories)}."
            });
        }

        return errors;
    }
}
