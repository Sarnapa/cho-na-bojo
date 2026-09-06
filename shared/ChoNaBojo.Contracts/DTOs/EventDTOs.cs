using ChoNaBojo.Contracts.Enums;

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

public sealed record EventListingQuery(
	int? SportId,
	DateTimeOffset? AvailableFromUtc,
	DateTimeOffset? AvailableToUtc);
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

public sealed record EventListItemResponse(
	Guid EventId,
	string Title,
	string? Description,
	DateTimeOffset StartsAtUtc,
	DateTimeOffset EstimatedEndsAtUtc,
	int ParticipantLimit,
	int ParticipantCount,
	bool AutoAccept,
	EventSportSummary Sport,
	bool IsOrganizer,
	EventJoinRequestStatus? CurrentUserRequestStatus);

public sealed record JoinRequestResponse(
	Guid RequestId,
	Guid EventId,
	EventJoinRequestStatus Status,
	DateTimeOffset CreatedUtc,
	DateTimeOffset? UpdatedUtc);

public sealed record EventConflictResponse(string Code, string? Field, string Message);
#endregion
