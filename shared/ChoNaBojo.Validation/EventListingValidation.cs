using ChoNaBojo.Contracts.DTOs;

namespace ChoNaBojo.Validation;

/// <summary>
/// Pure validation for venue event listing filters shared by the API and client.
/// </summary>
public static class EventListingValidation
{
	#region Public methods
	public static ValidationResult ValidateEventListingQuery(EventListingQuery query)
	{
		var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

		if (query.SportId is <= 0)
		{
			AddValidationError(errors, "sportId", "Sport id must be positive.");
		}

		if (query.AvailableFromUtc.HasValue != query.AvailableToUtc.HasValue)
		{
			string missingField = query.AvailableFromUtc.HasValue
				? "availableToUtc"
				: "availableFromUtc";
			AddValidationError(
				errors,
				missingField,
				"Availability start and end must be provided together.");
		}

		if (query.AvailableFromUtc is { Offset: var fromOffset } && fromOffset != TimeSpan.Zero)
		{
			AddValidationError(
				errors,
				"availableFromUtc",
				"Availability start must use a zero UTC offset.");
		}

		if (query.AvailableToUtc is { Offset: var toOffset } && toOffset != TimeSpan.Zero)
		{
			AddValidationError(
				errors,
				"availableToUtc",
				"Availability end must use a zero UTC offset.");
		}

		if (query.AvailableFromUtc.HasValue
			&& query.AvailableToUtc.HasValue
			&& query.AvailableFromUtc.Value >= query.AvailableToUtc.Value)
		{
			AddValidationError(
				errors,
				"availableToUtc",
				"Availability end must be after availability start.");
		}

		return errors.Count == 0 ? ValidationResult.Valid : new ValidationResult(errors);
	}
	#endregion

	#region Private methods
	private static void AddValidationError(IDictionary<string, string[]> errors, string key, string message)
	{
		if (errors.TryGetValue(key, out string[]? existing))
		{
			errors[key] = [.. existing, message];
			return;
		}

		errors[key] = [message];
	}
	#endregion
}
