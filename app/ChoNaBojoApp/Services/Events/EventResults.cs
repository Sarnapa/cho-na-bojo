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
