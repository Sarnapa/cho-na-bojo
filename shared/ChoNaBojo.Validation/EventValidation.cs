using ChoNaBojo.Contracts.Consts;
using ChoNaBojo.Contracts.DTOs;

namespace ChoNaBojo.Validation;

/// <summary>
/// Pure event creation validation shared by the authoritative API and client form.
/// Stateful reference, identity, idempotency, and local-time checks remain with their owners.
/// </summary>
public static class EventValidation
{
	#region Public methods
	public static ValidationResult ValidateCreateEventRequest(
		CreateEventRequest request,
		DateTimeOffset nowUtc)
	{
		var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
		string title = request.Title?.Trim() ?? string.Empty;
		string? description = string.IsNullOrWhiteSpace(request.Description)
			? null
			: request.Description.Trim();

		if (request.ClientRequestId == Guid.Empty)
		{
			AddValidationError(errors, "clientRequestId", "Client request id is required.");
		}

		if (request.VenueId <= 0)
		{
			AddValidationError(errors, "venueId", "Venue id must be positive.");
		}

		if (request.SportId <= 0)
		{
			AddValidationError(errors, "sportId", "Sport id must be positive.");
		}

		if (title.Length == 0)
		{
			AddValidationError(errors, "title", "Title is required.");
		}
		else if (title.Length > EventPolicy.TitleMaxLength)
		{
			AddValidationError(
				errors,
				"title",
				$"Title must not exceed {EventPolicy.TitleMaxLength} characters.");
		}

		if (description?.Length > EventPolicy.DescriptionMaxLength)
		{
			AddValidationError(
				errors,
				"description",
				$"Description must not exceed {EventPolicy.DescriptionMaxLength} characters.");
		}

		if (request.StartsAtUtc.Offset != TimeSpan.Zero)
		{
			AddValidationError(errors, "startsAtUtc", "Start time must use a zero UTC offset.");
		}

		if (request.EstimatedEndsAtUtc.Offset != TimeSpan.Zero)
		{
			AddValidationError(errors, "estimatedEndsAtUtc", "End time must use a zero UTC offset.");
		}

		if (request.ParticipantLimit < EventPolicy.ParticipantLimitMinimum
			|| request.ParticipantLimit > EventPolicy.ParticipantLimitMaximum)
		{
			AddValidationError(
				errors,
				"participantLimit",
				$"Participant limit must be between {EventPolicy.ParticipantLimitMinimum} and {EventPolicy.ParticipantLimitMaximum}.");
		}

		TimeSpan duration = request.EstimatedEndsAtUtc - request.StartsAtUtc;
		if (duration <= TimeSpan.Zero)
		{
			AddValidationError(errors, "estimatedEndsAtUtc", "End time must be after start time.");
		}
		else if (duration > EventPolicy.MaximumDuration)
		{
			AddValidationError(
				errors,
				"estimatedEndsAtUtc",
				$"Event duration must not exceed {EventPolicy.MaximumDuration.TotalHours:0} hours.");
		}

		if (request.StartsAtUtc < nowUtc.ToUniversalTime() - EventPolicy.StartClockSkewTolerance)
		{
			AddValidationError(errors, "startsAtUtc", "Start time cannot be in the past.");
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
