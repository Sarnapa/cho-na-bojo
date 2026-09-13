using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Validation;

namespace ChoNaBojo.UnitTests.Validation;

public sealed class EventValidationTests
{
	#region Private fields
	private static readonly DateTimeOffset NowUtc =
		new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
	#endregion

	#region Test methods
	[Theory]
	[InlineData(1, false)]
	[InlineData(2, true)]
	[InlineData(300, true)]
	[InlineData(301, false)]
	public void Participant_limit_uses_the_inclusive_two_to_three_hundred_boundary(
		int participantLimit,
		bool expectedValid)
	{
		CreateEventRequest request = CreateValidRequest() with
		{
			ParticipantLimit = participantLimit
		};

		ValidationResult result = EventValidation.ValidateCreateEventRequest(request, NowUtc);

		Assert.Equal(expectedValid, !result.Errors.ContainsKey("participantLimit"));
	}

	[Theory]
	[InlineData(-1, false)]
	[InlineData(0, false)]
	[InlineData(1, true)]
	[InlineData(86_400, true)]
	[InlineData(86_401, false)]
	public void End_time_must_follow_start_and_duration_must_not_exceed_twenty_four_hours(
		int durationSeconds,
		bool expectedValid)
	{
		CreateEventRequest baseline = CreateValidRequest();
		CreateEventRequest request = baseline with
		{
			EstimatedEndsAtUtc = baseline.StartsAtUtc.AddSeconds(durationSeconds)
		};

		ValidationResult result = EventValidation.ValidateCreateEventRequest(request, NowUtc);

		Assert.Equal(expectedValid, !result.Errors.ContainsKey("estimatedEndsAtUtc"));
	}

	[Theory]
	[InlineData(1, 0, "startsAtUtc")]
	[InlineData(0, 1, "estimatedEndsAtUtc")]
	public void Event_boundaries_require_zero_UTC_offset(
		int startOffsetHours,
		int endOffsetHours,
		string errorKey)
	{
		CreateEventRequest request = CreateValidRequest() with
		{
			StartsAtUtc = new DateTimeOffset(
				2030,
				1,
				1,
				13,
				0,
				0,
				TimeSpan.FromHours(startOffsetHours)),
			EstimatedEndsAtUtc = new DateTimeOffset(
				2030,
				1,
				1,
				15,
				0,
				0,
				TimeSpan.FromHours(endOffsetHours))
		};

		ValidationResult result = EventValidation.ValidateCreateEventRequest(request, NowUtc);

		Assert.False(result.IsValid);
		Assert.Contains(errorKey, result.Errors.Keys);
	}

	[Theory]
	[InlineData(-121, false)]
	[InlineData(-120, true)]
	[InlineData(0, true)]
	public void Start_clock_allows_exactly_two_minutes_of_tolerance(
		int secondsFromNow,
		bool expectedValid)
	{
		DateTimeOffset startsAtUtc = NowUtc.AddSeconds(secondsFromNow);
		CreateEventRequest request = CreateValidRequest() with
		{
			StartsAtUtc = startsAtUtc,
			EstimatedEndsAtUtc = startsAtUtc.AddHours(1)
		};

		ValidationResult result = EventValidation.ValidateCreateEventRequest(request, NowUtc);

		Assert.Equal(expectedValid, !result.Errors.ContainsKey("startsAtUtc"));
	}
	#endregion

	#region Private methods
	private static CreateEventRequest CreateValidRequest()
	{
		return new CreateEventRequest(
			Guid.Parse("11111111-1111-1111-1111-111111111111"),
			VenueId: 1,
			SportId: 1,
			Title: "Evening football",
			Description: "Meet by the north entrance",
			StartsAtUtc: NowUtc.AddHours(1),
			EstimatedEndsAtUtc: NowUtc.AddHours(2),
			ParticipantLimit: 2,
			AutoAccept: false);
	}
	#endregion
}
