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
