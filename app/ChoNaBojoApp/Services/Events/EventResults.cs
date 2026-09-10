using ChoNaBojo.Contracts.DTOs;

namespace ChoNaBojo.App.Services.Events;

#region CreateEventResultStatus
public enum CreateEventResultStatus
{
	Success,
	ValidationFailed,
	ReferenceChanged,
	Unauthorized,
	Network,
	Unknown
}
#endregion

#region VenueEventListResultStatus
public enum VenueEventListResultStatus
{
	Success,
	ValidationFailed,
	ReferenceChanged,
	Unauthorized,
	Network,
	Unknown
}
#endregion

#region VenueEventListResult
/// <summary>
/// Typed client outcome for <c>GET /api/venues/{venueId}/events</c>.
/// </summary>
public sealed record VenueEventListResult
{
	#region Properties
	public VenueEventListResultStatus Status { get; }
	public IReadOnlyList<EventListItemResponse>? Events { get; }
	public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; }
	public EventConflictResponse? Conflict { get; }
	#endregion

	#region Constructors
	private VenueEventListResult(
		VenueEventListResultStatus status,
		IReadOnlyList<EventListItemResponse>? events,
		IReadOnlyDictionary<string, string[]>? validationErrors,
		EventConflictResponse? conflict)
	{
		Status = status;
		Events = events;
		ValidationErrors = validationErrors;
		Conflict = conflict;
	}
	#endregion

	#region Public methods
	public static VenueEventListResult Success(IReadOnlyList<EventListItemResponse> events)
	{
		return new(VenueEventListResultStatus.Success, events, null, null);
	}

	public static VenueEventListResult ValidationFailed(
		IReadOnlyDictionary<string, string[]> errors)
	{
		return new(VenueEventListResultStatus.ValidationFailed, null, errors, null);
	}

	public static VenueEventListResult ReferenceChanged(EventConflictResponse conflict)
	{
		return new(VenueEventListResultStatus.ReferenceChanged, null, null, conflict);
	}

	public static VenueEventListResult Unauthorized()
	{
		return new(VenueEventListResultStatus.Unauthorized, null, null, null);
	}

	public static VenueEventListResult Network()
	{
		return new(VenueEventListResultStatus.Network, null, null, null);
	}

	public static VenueEventListResult Unknown()
	{
		return new(VenueEventListResultStatus.Unknown, null, null, null);
	}
	#endregion
}
#endregion

#region JoinEventResultStatus
public enum JoinEventResultStatus
{
	Success,
	Conflict,
	Unauthorized,
	Network,
	Unknown
}
#endregion

#region JoinEventResult
/// <summary>
/// Typed client outcome for <c>POST /api/events/{eventId}/join-requests</c>.
/// A replay is a successful response carrying the canonical stored request.
/// </summary>
public sealed record JoinEventResult
{
	#region Properties
	public JoinEventResultStatus Status { get; }
	public JoinRequestResponse? Response { get; }
	public bool IsReplay { get; }
	public EventConflictResponse? ConflictResponse { get; }
	#endregion

	#region Constructors
	private JoinEventResult(
		JoinEventResultStatus status,
		JoinRequestResponse? response,
		bool isReplay,
		EventConflictResponse? conflictResponse)
	{
		Status = status;
		Response = response;
		IsReplay = isReplay;
		ConflictResponse = conflictResponse;
	}
	#endregion

	#region Public methods
	public static JoinEventResult Success(JoinRequestResponse response, bool isReplay)
	{
		return new(JoinEventResultStatus.Success, response, isReplay, null);
	}

	public static JoinEventResult Conflict(EventConflictResponse conflict)
	{
		return new(JoinEventResultStatus.Conflict, null, false, conflict);
	}

	public static JoinEventResult Unauthorized()
	{
		return new(JoinEventResultStatus.Unauthorized, null, false, null);
	}

	public static JoinEventResult Network()
	{
		return new(JoinEventResultStatus.Network, null, false, null);
	}

	public static JoinEventResult Unknown()
	{
		return new(JoinEventResultStatus.Unknown, null, false, null);
	}
	#endregion
}
#endregion

#region MyEventsResultStatus
public enum MyEventsResultStatus
{
	Success,
	Unauthorized,
	Network,
	Unknown
}
#endregion

#region MyEventsResult
/// <summary>
/// Typed client outcome for <c>GET /api/me/events</c>.
/// </summary>
public sealed record MyEventsResult
{
	#region Properties
	public MyEventsResultStatus Status { get; }
	public MyEventsResponse? Response { get; }
	#endregion

	#region Constructors
	private MyEventsResult(MyEventsResultStatus status, MyEventsResponse? response)
	{
		Status = status;
		Response = response;
	}
	#endregion

	#region Public methods
	public static MyEventsResult Success(MyEventsResponse response)
	{
		return new(MyEventsResultStatus.Success, response);
	}

	public static MyEventsResult Unauthorized()
	{
		return new(MyEventsResultStatus.Unauthorized, null);
	}

	public static MyEventsResult Network()
	{
		return new(MyEventsResultStatus.Network, null);
	}

	public static MyEventsResult Unknown()
	{
		return new(MyEventsResultStatus.Unknown, null);
	}
	#endregion
}
#endregion

#region EventJoinRequestQueueResultStatus
public enum EventJoinRequestQueueResultStatus
{
	Success,
	NotFound,
	Unauthorized,
	Network,
	Unknown
}
#endregion

#region EventJoinRequestQueueResult
/// <summary>
/// Typed client outcome for <c>GET /api/events/{eventId}/join-requests</c>.
/// </summary>
public sealed record EventJoinRequestQueueResult
{
	#region Properties
	public EventJoinRequestQueueResultStatus Status { get; }
	public IReadOnlyList<EventJoinRequestQueueItemResponse>? Requests { get; }
	public EventConflictResponse? NotFoundResponse { get; }
	#endregion

	#region Constructors
	private EventJoinRequestQueueResult(
		EventJoinRequestQueueResultStatus status,
		IReadOnlyList<EventJoinRequestQueueItemResponse>? requests,
		EventConflictResponse? notFoundResponse)
	{
		Status = status;
		Requests = requests;
		NotFoundResponse = notFoundResponse;
	}
	#endregion

	#region Public methods
	public static EventJoinRequestQueueResult Success(
		IReadOnlyList<EventJoinRequestQueueItemResponse> requests)
	{
		return new(EventJoinRequestQueueResultStatus.Success, requests, null);
	}

	public static EventJoinRequestQueueResult NotFound(EventConflictResponse conflict)
	{
		return new(EventJoinRequestQueueResultStatus.NotFound, null, conflict);
	}

	public static EventJoinRequestQueueResult Unauthorized()
	{
		return new(EventJoinRequestQueueResultStatus.Unauthorized, null, null);
	}

	public static EventJoinRequestQueueResult Network()
	{
		return new(EventJoinRequestQueueResultStatus.Network, null, null);
	}

	public static EventJoinRequestQueueResult Unknown()
	{
		return new(EventJoinRequestQueueResultStatus.Unknown, null, null);
	}
	#endregion
}
#endregion

#region EventContactsResultStatus
public enum EventContactsResultStatus
{
	Success,
	NotFound,
	Unauthorized,
	Network,
	Unknown
}
#endregion

#region EventContactsResult
/// <summary>
/// Typed client outcome for <c>GET /api/events/{eventId}/contacts</c>.
/// </summary>
public sealed record EventContactsResult
{
	#region Properties
	public EventContactsResultStatus Status { get; }
	public EventContactsResponse? Response { get; }
	#endregion

	#region Constructors
	private EventContactsResult(
		EventContactsResultStatus status,
		EventContactsResponse? response)
	{
		Status = status;
		Response = response;
	}
	#endregion

	#region Public methods
	public static EventContactsResult Success(EventContactsResponse response)
	{
		return new(EventContactsResultStatus.Success, response);
	}

	public static EventContactsResult NotFound()
	{
		return new(EventContactsResultStatus.NotFound, null);
	}

	public static EventContactsResult Unauthorized()
	{
		return new(EventContactsResultStatus.Unauthorized, null);
	}

	public static EventContactsResult Network()
	{
		return new(EventContactsResultStatus.Network, null);
	}

	public static EventContactsResult Unknown()
	{
		return new(EventContactsResultStatus.Unknown, null);
	}
	#endregion
}
#endregion

#region ResolveJoinRequestResultStatus
public enum ResolveJoinRequestResultStatus
{
	Success,
	Conflict,
	NotFound,
	Unauthorized,
	Network,
	Unknown
}
#endregion

#region ResolveJoinRequestResult
/// <summary>
/// Typed client outcome shared by organizer accept and reject calls.
/// </summary>
public sealed record ResolveJoinRequestResult
{
	#region Properties
	public ResolveJoinRequestResultStatus Status { get; }
	public JoinRequestResponse? Response { get; }
	public EventConflictResponse? ConflictResponse { get; }
	#endregion

	#region Constructors
	private ResolveJoinRequestResult(
		ResolveJoinRequestResultStatus status,
		JoinRequestResponse? response,
		EventConflictResponse? conflictResponse)
	{
		Status = status;
		Response = response;
		ConflictResponse = conflictResponse;
	}
	#endregion

	#region Public methods
	public static ResolveJoinRequestResult Success(JoinRequestResponse response)
	{
		return new(ResolveJoinRequestResultStatus.Success, response, null);
	}

	public static ResolveJoinRequestResult Conflict(EventConflictResponse conflict)
	{
		return new(ResolveJoinRequestResultStatus.Conflict, null, conflict);
	}

	public static ResolveJoinRequestResult NotFound(EventConflictResponse conflict)
	{
		return new(ResolveJoinRequestResultStatus.NotFound, null, conflict);
	}

	public static ResolveJoinRequestResult Unauthorized()
	{
		return new(ResolveJoinRequestResultStatus.Unauthorized, null, null);
	}

	public static ResolveJoinRequestResult Network()
	{
		return new(ResolveJoinRequestResultStatus.Network, null, null);
	}

	public static ResolveJoinRequestResult Unknown()
	{
		return new(ResolveJoinRequestResultStatus.Unknown, null, null);
	}
	#endregion
}
#endregion

#region CreateEventResult
/// <summary>
/// Typed client outcome for <c>POST /api/events</c>. Transport details never escape to the
/// event form, and an existing idempotent event is represented as a normal replay success.
/// </summary>
public sealed record CreateEventResult
{
	#region Properties
	public CreateEventResultStatus Status { get; }
	public CreatedEventResponse? Response { get; }
	public bool IsReplay { get; }
	public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; }
	public EventConflictResponse? Conflict { get; }
	#endregion

	#region Constructors
	private CreateEventResult(
		CreateEventResultStatus status,
		CreatedEventResponse? response,
		bool isReplay,
		IReadOnlyDictionary<string, string[]>? validationErrors,
		EventConflictResponse? conflict)
	{
		Status = status;
		Response = response;
		IsReplay = isReplay;
		ValidationErrors = validationErrors;
		Conflict = conflict;
	}
	#endregion

	#region Public methods
	public static CreateEventResult Success(CreatedEventResponse response, bool isReplay)
	{
		return new(CreateEventResultStatus.Success, response, isReplay, null, null);
	}

	public static CreateEventResult ValidationFailed(
		IReadOnlyDictionary<string, string[]> errors)
	{
		return new(CreateEventResultStatus.ValidationFailed, null, false, errors, null);
	}

	public static CreateEventResult ReferenceChanged(EventConflictResponse conflict)
	{
		return new(CreateEventResultStatus.ReferenceChanged, null, false, null, conflict);
	}

	public static CreateEventResult Unauthorized()
	{
		return new(CreateEventResultStatus.Unauthorized, null, false, null, null);
	}

	public static CreateEventResult Network()
	{
		return new(CreateEventResultStatus.Network, null, false, null, null);
	}

	public static CreateEventResult Unknown()
	{
		return new(CreateEventResultStatus.Unknown, null, false, null, null);
	}
	#endregion
}
#endregion
