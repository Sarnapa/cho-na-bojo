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

public sealed record OrganizedEventResponse(
	Guid EventId,
	string Title,
	DateTimeOffset StartsAtUtc,
	DateTimeOffset EstimatedEndsAtUtc,
	int ParticipantLimit,
	int ParticipantCount,
	bool AutoAccept,
	EventVenueSummary Venue,
	EventSportSummary Sport,
	int PendingRequestCount);

public sealed record RequestedEventResponse(
	Guid EventId,
	Guid JoinRequestId,
	string Title,
	DateTimeOffset StartsAtUtc,
	DateTimeOffset EstimatedEndsAtUtc,
	int ParticipantLimit,
	int ParticipantCount,
	bool AutoAccept,
	EventVenueSummary Venue,
	EventSportSummary Sport,
	EventJoinRequestStatus Status,
	DateTimeOffset? UpdatedUtc);

public sealed record MyEventsResponse(
	IReadOnlyList<OrganizedEventResponse> OrganizedEvents,
	IReadOnlyList<RequestedEventResponse> RequestedEvents);

public sealed record EventJoinRequestQueueItemResponse(
	Guid RequestId,
	string RequesterDisplayKey,
	EventJoinRequestStatus Status,
	DateTimeOffset CreatedUtc,
	DateTimeOffset? UpdatedUtc);

public sealed record ContactInfoResponse(
	string? Phone,
	string? Email,
	CommunicatorPlatform? CommunicatorPlatform,
	string? CommunicatorHandle);

public sealed record EventContactResponse(
	Guid UserId,
	Guid? JoinRequestId,
	bool IsOrganizer,
	ContactInfoResponse Contact);

public sealed record EventContactsResponse(
	IReadOnlyList<EventContactResponse> Contacts);

public sealed record EventConflictResponse(string Code, string? Field, string Message);
#endregion
