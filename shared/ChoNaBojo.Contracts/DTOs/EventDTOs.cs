namespace ChoNaBojo.Contracts.DTOs;

#region Requests DTOs
public sealed record CreateEventRequest(
	Guid ClientRequestId,
	int VenueId,
	int SportId,
	string Title,
	string? Description,
	DateTimeOffset StartsAtUtc,
	DateTimeOffset EstimatedEndsAtUtc,
	int ParticipantLimit,
	bool AutoAccept);
#endregion

#region Responses DTOs
public sealed record CreatedEventResponse(
	Guid Id,
	string Title,
	string? Description,
	DateTimeOffset StartsAtUtc,
	DateTimeOffset EstimatedEndsAtUtc,
	DateTimeOffset CreatedUtc,
	int ParticipantLimit,
	int ParticipantCount,
	bool AutoAccept,
	EventVenueSummary Venue,
	EventSportSummary Sport);

public sealed record EventVenueSummary(int Id, string Name, string Address);

public sealed record EventSportSummary(int Id, string Code, string Name);

public sealed record EventConflictResponse(string Code, string? Field, string Message);
#endregion
