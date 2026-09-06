namespace ChoNaBojo.App.Services.Events;

#region EventAvailabilityPreset
public enum EventAvailabilityPreset
{
	AnyTime,
	Today,
	Tomorrow,
	NextSevenDays,
	Custom
}
#endregion

#region EventAvailabilityWindow
public sealed record EventAvailabilityWindow(
	DateTimeOffset AvailableFromUtc,
	DateTimeOffset AvailableToUtc);
#endregion

#region EventAvailabilityConversionResult
public sealed record EventAvailabilityConversionResult
{
	#region Properties
	public EventAvailabilityWindow? Window { get; }
	public IReadOnlyDictionary<string, string[]> Errors { get; }
	public bool IsValid => Errors.Count == 0;
	#endregion

	#region Constructors
	private EventAvailabilityConversionResult(
		EventAvailabilityWindow? window,
		IReadOnlyDictionary<string, string[]> errors)
	{
		Window = window;
		Errors = errors;
	}
	#endregion

	#region Public methods
	public static EventAvailabilityConversionResult Success(
		EventAvailabilityWindow? window)
	{
		return new(
			window,
			new Dictionary<string, string[]>(StringComparer.Ordinal));
	}

	public static EventAvailabilityConversionResult Failed(
		IReadOnlyDictionary<string, string[]> errors)
	{
		return new(null, errors);
	}
	#endregion
}
#endregion

/// <summary>
/// Converts local calendar availability into unambiguous zero-offset UTC intervals.
/// </summary>
public static class EventAvailabilityConversion
{
	#region Public methods
	public static EventAvailabilityConversionResult ForPreset(
		EventAvailabilityPreset preset)
	{
		return ForPreset(preset, DateTime.Now, TimeZoneInfo.Local);
	}

	public static EventAvailabilityConversionResult ForPreset(
		EventAvailabilityPreset preset,
		DateTime selectionTime,
		TimeZoneInfo timeZone)
	{
		ArgumentNullException.ThrowIfNull(timeZone);

		if (preset == EventAvailabilityPreset.AnyTime)
		{
			return EventAvailabilityConversionResult.Success(null);
		}

		DateTime startDate = preset switch
		{
			EventAvailabilityPreset.Today => selectionTime.Date,
			EventAvailabilityPreset.Tomorrow => selectionTime.Date.AddDays(1),
			EventAvailabilityPreset.NextSevenDays => selectionTime.Date,
			_ => throw new ArgumentOutOfRangeException(
				nameof(preset),
				preset,
				"Custom availability must be converted from explicit values.")
		};
		DateTime endDate = preset switch
		{
			EventAvailabilityPreset.Today => startDate.AddDays(1),
			EventAvailabilityPreset.Tomorrow => startDate.AddDays(1),
			EventAvailabilityPreset.NextSevenDays => startDate.AddDays(7),
			_ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
		};

		return ConvertLocalRange(startDate, endDate, timeZone);
	}

	public static EventAvailabilityConversionResult ConvertCustomToUtc(
		DateTime startDate,
		TimeSpan startTime,
		DateTime endDate,
		TimeSpan endTime)
	{
		return ConvertCustomToUtc(
			startDate,
			startTime,
			endDate,
			endTime,
			TimeZoneInfo.Local);
	}

	public static EventAvailabilityConversionResult ConvertCustomToUtc(
		DateTime startDate,
		TimeSpan startTime,
		DateTime endDate,
		TimeSpan endTime,
		TimeZoneInfo timeZone)
	{
		ArgumentNullException.ThrowIfNull(timeZone);

		var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
		ValidateClockValue(
			startTime,
			"availableFromUtc",
			"Availability start time",
			errors);
		ValidateClockValue(
			endTime,
			"availableToUtc",
			"Availability end time",
			errors);
		if (errors.Count > 0)
		{
			return EventAvailabilityConversionResult.Failed(errors);
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
				"availableToUtc",
				"Availability end must be after availability start.");
			return EventAvailabilityConversionResult.Failed(errors);
		}

		return ConvertLocalRange(localStart, localEnd, timeZone);
	}
	#endregion

	#region Private methods
	private static EventAvailabilityConversionResult ConvertLocalRange(
		DateTime localStart,
		DateTime localEnd,
		TimeZoneInfo timeZone)
	{
		localStart = DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified);
		localEnd = DateTime.SpecifyKind(localEnd, DateTimeKind.Unspecified);

		var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
		ValidateWallTime(
			localStart,
			timeZone,
			"availableFromUtc",
			"Availability start",
			errors);
		ValidateWallTime(
			localEnd,
			timeZone,
			"availableToUtc",
			"Availability end",
			errors);
		if (errors.Count > 0)
		{
			return EventAvailabilityConversionResult.Failed(errors);
		}

		var fromUtc = new DateTimeOffset(
			TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone));
		var toUtc = new DateTimeOffset(
			TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone));
		if (fromUtc >= toUtc)
		{
			AddError(
				errors,
				"availableToUtc",
				"Availability end must be after availability start.");
			return EventAvailabilityConversionResult.Failed(errors);
		}

		return EventAvailabilityConversionResult.Success(
			new EventAvailabilityWindow(fromUtc, toUtc));
	}

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
