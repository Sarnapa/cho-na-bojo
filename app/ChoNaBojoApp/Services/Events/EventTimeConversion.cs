using ChoNaBojo.Contracts.Consts;

namespace ChoNaBojo.App.Services.Events;

#region EventTimeConversionResult
public sealed record EventTimeConversionResult
{
	#region Properties
	public DateTimeOffset? StartsAtUtc { get; }
	public DateTimeOffset? EstimatedEndsAtUtc { get; }
	public IReadOnlyDictionary<string, string[]> Errors { get; }
	public bool IsValid => Errors.Count == 0;
	#endregion

	#region Constructors
	private EventTimeConversionResult(
		DateTimeOffset? startsAtUtc,
		DateTimeOffset? estimatedEndsAtUtc,
		IReadOnlyDictionary<string, string[]> errors)
	{
		StartsAtUtc = startsAtUtc;
		EstimatedEndsAtUtc = estimatedEndsAtUtc;
		Errors = errors;
	}
	#endregion

	#region Public methods
	public static EventTimeConversionResult Success(
		DateTimeOffset startsAtUtc,
		DateTimeOffset estimatedEndsAtUtc)
	{
		return new(
			startsAtUtc,
			estimatedEndsAtUtc,
			new Dictionary<string, string[]>(StringComparer.Ordinal));
	}

	public static EventTimeConversionResult Failed(
		IReadOnlyDictionary<string, string[]> errors)
	{
		return new(null, null, errors);
	}
	#endregion
}
#endregion

/// <summary>
/// Converts device-local wall-clock input into unambiguous UTC instants. Invalid and
/// ambiguous daylight-saving times are rejected rather than guessed.
/// </summary>
public static class EventTimeConversion
{
	#region Public methods
	public static EventTimeConversionResult ConvertToUtc(
		DateTime startDate,
		TimeSpan startTime,
		DateTime endDate,
		TimeSpan endTime)
	{
		return ConvertToUtc(startDate, startTime, endDate, endTime, TimeZoneInfo.Local);
	}

	public static EventTimeConversionResult ConvertToUtc(
		DateTime startDate,
		TimeSpan startTime,
		DateTime endDate,
		TimeSpan endTime,
		TimeZoneInfo timeZone)
	{
		ArgumentNullException.ThrowIfNull(timeZone);

		var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
		ValidateClockValue(startTime, "startsAtUtc", "Start time", errors);
		ValidateClockValue(endTime, "estimatedEndsAtUtc", "End time", errors);
		if (errors.Count > 0)
		{
			return EventTimeConversionResult.Failed(errors);
		}

		DateTime localStart = DateTime.SpecifyKind(
			startDate.Date.Add(startTime),
			DateTimeKind.Unspecified);
		DateTime localEnd = DateTime.SpecifyKind(
			endDate.Date.Add(endTime),
			DateTimeKind.Unspecified);
		if (localEnd <= localStart)
		{
			AddError(
				errors,
				"estimatedEndsAtUtc",
				"End date and time must be after start date and time.");
			return EventTimeConversionResult.Failed(errors);
		}

		ValidateWallTime(localStart, timeZone, "startsAtUtc", "Start time", errors);
		ValidateWallTime(
			localEnd,
			timeZone,
			"estimatedEndsAtUtc",
			"End time",
			errors);
		if (errors.Count > 0)
		{
			return EventTimeConversionResult.Failed(errors);
		}

		var startsAtUtc = new DateTimeOffset(
			TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone));
		var estimatedEndsAtUtc = new DateTimeOffset(
			TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone));
		TimeSpan elapsed = estimatedEndsAtUtc - startsAtUtc;

		if (elapsed <= TimeSpan.Zero)
		{
			AddError(
				errors,
				"estimatedEndsAtUtc",
				"End time must be after start time.");
		}
		else if (elapsed > EventPolicy.MaximumDuration)
		{
			AddError(
				errors,
				"estimatedEndsAtUtc",
				$"Event duration must not exceed {EventPolicy.MaximumDuration.TotalHours:0} hours.");
		}

		return errors.Count == 0
			? EventTimeConversionResult.Success(startsAtUtc, estimatedEndsAtUtc)
			: EventTimeConversionResult.Failed(errors);
	}
	#endregion

	#region Private methods
	private static void ValidateClockValue(
		TimeSpan value,
		string field,
		string label,
		IDictionary<string, string[]> errors)
	{
		if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1))
		{
			AddError(errors, field, $"{label} must be a time within one day.");
		}
	}

	private static void ValidateWallTime(
		DateTime value,
		TimeZoneInfo timeZone,
		string field,
		string label,
		IDictionary<string, string[]> errors)
	{
		if (timeZone.IsInvalidTime(value))
		{
			AddError(
				errors,
				field,
				$"{label} does not exist in the current time zone because the clock moves forward.");
		}
		else if (timeZone.IsAmbiguousTime(value))
		{
			AddError(
				errors,
				field,
				$"{label} occurs twice in the current time zone. Choose another time.");
		}
	}

	private static void AddError(
		IDictionary<string, string[]> errors,
		string field,
		string message)
	{
		errors[field] = [message];
	}
	#endregion
}
