using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Mail;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChoNaBojo.App.Services;
using ChoNaBojo.App.Services.Events;
using ChoNaBojo.App.Services.Feedback;
using ChoNaBojo.Contracts.Consts;
using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Contracts.Enums;

namespace ChoNaBojo.App.ViewModels;

#region OrganizedEventViewData
public sealed record OrganizedEventViewData(
	Guid EventId,
	string Title,
	DateTimeOffset StartsAtUtc,
	DateTimeOffset EstimatedEndsAtUtc,
	int ParticipantLimit,
	int ParticipantCount,
	bool AutoAccept,
	string VenueName,
	string VenueAddress,
	string SportName,
	int PendingRequestCount,
	EventStatus EventStatus,
	bool IsEndedByServer)
{
	public bool IsEnded =>
		EventStatus == EventStatus.Closed
		|| IsEndedByServer
		|| EstimatedEndsAtUtc <= DateTimeOffset.UtcNow;
	public bool IsHistory =>
		EventStatus is EventStatus.Cancelled or EventStatus.Closed || IsEnded;
	public bool IsCancelled => EventStatus == EventStatus.Cancelled;
	public bool CanCancel => EventStatus == EventStatus.Active && !IsEnded;
	public bool IsFull => ParticipantCount >= ParticipantLimit;
	public double CardOpacity => IsHistory ? 0.6 : 1;
	public string StartsAtDisplay => MyEventsFormatting.FormatLocalDateTime(StartsAtUtc);
	public string EstimatedEndsAtDisplay => MyEventsFormatting.FormatLocalDateTime(
		EstimatedEndsAtUtc);
	public string ParticipantDisplay => string.Create(
		CultureInfo.CurrentCulture,
		$"{ParticipantCount} / {ParticipantLimit}");
	public string PendingRequestDisplay => PendingRequestCount == 1
		? "1 pending request"
		: string.Create(
			CultureInfo.CurrentCulture,
			$"{PendingRequestCount} pending requests");
	public string DisplayLabel => EventStatus switch
	{
		EventStatus.Cancelled => "Cancelled",
		EventStatus.Closed => "Finished",
		_ when IsEnded => "Finished",
		_ when IsFull => "Full",
		_ => "Open"
	};
	public string OpenRequestsLabel => PendingRequestCount == 0
		? "View requests"
		: string.Create(
			CultureInfo.CurrentCulture,
			$"View requests ({PendingRequestCount})");
	public string SemanticDescription =>
		$"{Title}, {DisplayLabel}, {ParticipantDisplay} participants, {PendingRequestDisplay}";

	public static OrganizedEventViewData FromResponse(OrganizedEventResponse response)
	{
		return new(
			response.EventId,
			response.Title,
			response.StartsAtUtc,
			response.EstimatedEndsAtUtc,
			response.ParticipantLimit,
			response.ParticipantCount,
			response.AutoAccept,
			response.Venue.Name,
			response.Venue.Address,
			response.Sport.Name,
			response.PendingRequestCount,
			response.EventStatus,
			IsEndedByServer: false);
	}
}
#endregion

#region RequestedEventViewData
public sealed record RequestedEventViewData(
	Guid EventId,
	Guid JoinRequestId,
	string Title,
	DateTimeOffset StartsAtUtc,
	DateTimeOffset EstimatedEndsAtUtc,
	int ParticipantLimit,
	int ParticipantCount,
	bool AutoAccept,
	string VenueName,
	string VenueAddress,
	string SportName,
	EventJoinRequestStatus Status,
	DateTimeOffset? UpdatedUtc,
	EventStatus EventStatus,
	bool IsActionInFlight,
	bool HasFetchedContactPayload,
	bool IsContactActionInFlight,
	IReadOnlyList<ContactMethodViewData> ContactRows)
{
	public bool IsEnded =>
		EventStatus == EventStatus.Closed
		|| EstimatedEndsAtUtc <= DateTimeOffset.UtcNow;
	public bool IsHistory =>
		EventStatus is EventStatus.Cancelled or EventStatus.Closed
		|| IsEnded
		|| Status is not (
			EventJoinRequestStatus.Pending or EventJoinRequestStatus.Accepted);
	public bool IsCancelled =>
		EventStatus == EventStatus.Cancelled
		|| Status == EventJoinRequestStatus.Cancelled;
	public bool IsFull => ParticipantCount >= ParticipantLimit;
	public double CardOpacity => IsHistory ? 0.6 : 1;
	public bool CanLeave =>
		!IsHistory
		&& Status == EventJoinRequestStatus.Accepted
		&& !IsActionInFlight;
	public bool IsContactAllowed => EventStatus != EventStatus.Cancelled;
	public bool HasRevealedContact =>
		IsContactAllowed
		&&
		Status == EventJoinRequestStatus.Accepted
		&& HasFetchedContactPayload;
	public bool IsContactLocked =>
		!IsContactAllowed
		|| Status != EventJoinRequestStatus.Accepted;
	public bool CanLoadContact =>
		IsContactAllowed
		&&
		Status == EventJoinRequestStatus.Accepted
		&& !HasFetchedContactPayload
		&& !IsContactActionInFlight;
	public bool ShowContactLoadAction =>
		IsContactAllowed
		&&
		Status == EventJoinRequestStatus.Accepted
		&& !HasFetchedContactPayload;
	public string ContactActionLabel => IsContactActionInFlight
		? "Loading contact..."
		: "View contact";
	public string StartsAtDisplay => MyEventsFormatting.FormatLocalDateTime(StartsAtUtc);
	public string EstimatedEndsAtDisplay => MyEventsFormatting.FormatLocalDateTime(
		EstimatedEndsAtUtc);
	public string ParticipantDisplay => string.Create(
		CultureInfo.CurrentCulture,
		$"{ParticipantCount} / {ParticipantLimit}");
	public string DisplayLabel => EventStatus switch
	{
		EventStatus.Cancelled => "Cancelled",
		EventStatus.Closed => "Finished",
		_ when IsEnded => "Finished",
		_ => Status switch
		{
			EventJoinRequestStatus.Pending => "Request pending",
			EventJoinRequestStatus.Accepted => "Joined",
			EventJoinRequestStatus.Rejected => "Rejected",
			EventJoinRequestStatus.Left => "Left",
			EventJoinRequestStatus.Removed => "Removed",
			EventJoinRequestStatus.Cancelled => "Event cancelled",
			_ => "Status unavailable"
		}
	};
	public string ActionLabel => IsActionInFlight ? "Leaving..." : "Leave";
	public string SemanticDescription =>
		$"{Title}, {DisplayLabel}, {ParticipantDisplay} participants";

	public static RequestedEventViewData FromResponse(RequestedEventResponse response)
	{
		return new(
			response.EventId,
			response.JoinRequestId,
			response.Title,
			response.StartsAtUtc,
			response.EstimatedEndsAtUtc,
			response.ParticipantLimit,
			response.ParticipantCount,
			response.AutoAccept,
			response.Venue.Name,
			response.Venue.Address,
			response.Sport.Name,
			response.Status,
			response.UpdatedUtc,
			response.EventStatus,
			IsActionInFlight: false,
			HasFetchedContactPayload: false,
			IsContactActionInFlight: false,
			ContactRows: []);
	}
}
#endregion

#region HistoryEventViewData
public sealed record HistoryEventViewData(
	Guid EventId,
	bool IsOrganized,
	string Title,
	DateTimeOffset StartsAtUtc,
	DateTimeOffset EstimatedEndsAtUtc,
	string VenueName,
	string VenueAddress,
	string SportName,
	string ParticipantDisplay,
	string DisplayLabel,
	bool IsCancelled)
{
	public double CardOpacity => 0.6;
	public string RoleLabel => IsOrganized ? "Organized" : "Requested";
	public string StartsAtDisplay => MyEventsFormatting.FormatLocalDateTime(StartsAtUtc);
	public string EstimatedEndsAtDisplay => MyEventsFormatting.FormatLocalDateTime(
		EstimatedEndsAtUtc);
	public string SemanticDescription =>
		$"{Title}, {RoleLabel}, {DisplayLabel}, {ParticipantDisplay} participants";

	public static HistoryEventViewData FromOrganized(OrganizedEventViewData item)
	{
		return new(
			item.EventId,
			IsOrganized: true,
			item.Title,
			item.StartsAtUtc,
			item.EstimatedEndsAtUtc,
			item.VenueName,
			item.VenueAddress,
			item.SportName,
			item.ParticipantDisplay,
			item.DisplayLabel,
			item.IsCancelled);
	}

	public static HistoryEventViewData FromRequested(RequestedEventViewData item)
	{
		return new(
			item.EventId,
			IsOrganized: false,
			item.Title,
			item.StartsAtUtc,
			item.EstimatedEndsAtUtc,
			item.VenueName,
			item.VenueAddress,
			item.SportName,
			item.ParticipantDisplay,
			item.DisplayLabel,
			item.IsCancelled);
	}
}
#endregion

#region EventJoinRequestViewData
public sealed record EventJoinRequestViewData(
	Guid RequestId,
	string RequesterDisplayKey,
	EventJoinRequestStatus Status,
	DateTimeOffset CreatedUtc,
	DateTimeOffset? UpdatedUtc,
	bool AcceptAllowedByEvent,
	EventStatus EventStatus,
	bool IsEventEnded,
	bool IsActionInFlight,
	bool HasFetchedContactPayload,
	IReadOnlyList<ContactMethodViewData> ContactRows)
{
	public bool IsPending => Status == EventJoinRequestStatus.Pending;
	public bool IsResolved => !IsPending;
	public bool IsEventActive => EventStatus == EventStatus.Active;
	public bool IsContactAllowed => EventStatus != EventStatus.Cancelled;
	public bool CanAccept =>
		IsPending
		&& IsEventActive
		&& !IsEventEnded
		&& AcceptAllowedByEvent
		&& !IsActionInFlight;
	public bool CanReject =>
		IsPending
		&& IsEventActive
		&& !IsEventEnded
		&& !IsActionInFlight;
	public bool CanRemove =>
		Status == EventJoinRequestStatus.Accepted
		&& IsEventActive
		&& !IsEventEnded
		&& !IsActionInFlight;
	public bool HasRevealedContact =>
		IsContactAllowed
		&&
		Status == EventJoinRequestStatus.Accepted
		&& HasFetchedContactPayload;
	public bool IsContactLocked =>
		!IsContactAllowed || Status != EventJoinRequestStatus.Accepted;
	public string CreatedDisplay => MyEventsFormatting.FormatLocalDateTime(CreatedUtc);
	public string StatusLabel => IsActionInFlight
		? "Updating..."
		: Status switch
		{
			EventJoinRequestStatus.Pending => "Pending",
			EventJoinRequestStatus.Accepted => "Accepted",
			EventJoinRequestStatus.Rejected => "Rejected",
			EventJoinRequestStatus.Left => "Left",
			EventJoinRequestStatus.Removed => "Removed",
			EventJoinRequestStatus.Cancelled => "Event cancelled",
			_ => "Status unavailable"
		};
	public string SemanticDescription =>
		$"{RequesterDisplayKey}, requested {CreatedDisplay}, {StatusLabel}";

	public static EventJoinRequestViewData FromResponse(
		EventJoinRequestQueueItemResponse response,
		OrganizedEventViewData organizedEvent)
	{
		return new(
			response.RequestId,
			response.RequesterDisplayKey,
			response.Status,
			response.CreatedUtc,
			response.UpdatedUtc,
			!organizedEvent.IsFull,
			organizedEvent.EventStatus,
			organizedEvent.IsEnded,
			IsActionInFlight: false,
			HasFetchedContactPayload: false,
			ContactRows: []);
	}
}
#endregion

#region ContactMethodViewData
public enum ContactMethodKind
{
	Phone,
	Email,
	Communicator
}

public sealed record ContactMethodViewData(
	ContactMethodKind Kind,
	string DisplayValue,
	Uri? LaunchUri,
	string IconKey)
{
	public bool CanLaunch => LaunchUri is not null;
	public bool IsPhone => Kind == ContactMethodKind.Phone;
	public bool IsEmail => Kind == ContactMethodKind.Email;
	public bool IsCommunicator => Kind == ContactMethodKind.Communicator;
	public string CopySemanticDescription => $"Copy {KindLabel}";
	public string OpenSemanticDescription => $"Open {KindLabel}";
	private string KindLabel => Kind switch
	{
		ContactMethodKind.Phone => "phone number",
		ContactMethodKind.Email => "email address",
		_ => "messenger contact"
	};
}
#endregion

internal static class MyEventsFormatting
{
	public static string FormatLocalDateTime(DateTimeOffset utcValue)
	{
		DateTimeOffset localValue = TimeZoneInfo.ConvertTime(
			utcValue,
			TimeZoneInfo.Local);
		return localValue.ToString(
			"ddd, d MMM yyyy, HH:mm",
			CultureInfo.CurrentCulture);
	}
}

public partial class MyEventsViewModel : ViewModelBase
{
	#region Private fields
	private readonly IApiService _apiService;
	private readonly IFeedbackService _feedbackService;
	private CancellationTokenSource? _queueCancellation;
	private CancellationTokenSource? _resolutionCancellation;
	private CancellationTokenSource? _contactCancellation;
	private EventContactsResponse? _selectedOrganizerContactPayload;
	private EventContactsResponse? _selectedRequestedContactPayload;
	private Guid? _selectedRequestedContactEventId;
	private long _contactLoadVersion;
	private bool _isConfirmationInProgress;
	#endregion

	#region Observable properties
	[ObservableProperty]
	private ObservableCollection<OrganizedEventViewData> organizedEvents = [];

	[ObservableProperty]
	private ObservableCollection<RequestedEventViewData> requestedEvents = [];

	[ObservableProperty]
	private ObservableCollection<HistoryEventViewData> historyEvents = [];

	[ObservableProperty]
	private bool isHistorySectionExpanded;

	[ObservableProperty]
	private ObservableCollection<EventJoinRequestViewData> requestQueue = [];

	[ObservableProperty]
	private OrganizedEventViewData? selectedOrganizedEvent;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ShowInitialLoading))]
	private bool isLoading;

	[ObservableProperty]
	private bool isRefreshing;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ShowQueueLoading))]
	private bool isQueueLoading;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ShowListError))]
	private bool hasListError;

	[ObservableProperty]
	private string listErrorMessage = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ShowQueueError))]
	private bool hasQueueError;

	[ObservableProperty]
	private string queueErrorMessage = string.Empty;
	#endregion

	#region Constructors
	public MyEventsViewModel(
		IApiService apiService,
		IFeedbackService feedbackService)
	{
		_apiService = apiService;
		_feedbackService = feedbackService;
	}
	#endregion

	#region Public properties
	public bool IsListVisible => SelectedOrganizedEvent is null;
	public bool IsDetailVisible => SelectedOrganizedEvent is not null;
	public bool HasOrganizedEvents => OrganizedEvents.Count > 0;
	public bool HasRequestedEvents => RequestedEvents.Count > 0;
	public bool HasHistoryEvents => HistoryEvents.Count > 0;
	public bool HasNoOrganizedEvents => !HasOrganizedEvents;
	public bool HasNoRequestedEvents => !HasRequestedEvents;
	public bool ShowNoOrganizedEvents => HasNoOrganizedEvents && HasRequestedEvents;
	public bool ShowNoRequestedEvents => HasNoRequestedEvents && HasOrganizedEvents;
	public bool HasNoEvents =>
		!HasOrganizedEvents && !HasRequestedEvents && !HasHistoryEvents;
	public bool ShowHistoryEvents => HasHistoryEvents && IsHistorySectionExpanded;
	public string HistoryToggleLabel => IsHistorySectionExpanded
		? string.Create(
			CultureInfo.CurrentCulture,
			$"Hide history ({HistoryEvents.Count})")
		: string.Create(
			CultureInfo.CurrentCulture,
			$"History ({HistoryEvents.Count})");
	public bool HasQueuedRequests => RequestQueue.Count > 0;
	public bool HasNoQueuedRequests =>
		IsDetailVisible
		&& !IsQueueLoading
		&& !HasQueueError
		&& !HasQueuedRequests;
	public bool ShowInitialLoading => IsLoading && HasNoEvents && !HasListError;
	public bool ShowListError => HasListError && IsListVisible;
	public bool ShowQueueLoading =>
		IsQueueLoading && IsDetailVisible && !HasQueuedRequests && !HasQueueError;
	public bool ShowQueueError => HasQueueError && IsDetailVisible;
	public bool IsConfirmationInProgress => _isConfirmationInProgress;
	#endregion

	#region Commands
	[RelayCommand(CanExecute = nameof(CanRefresh))]
	private async Task RefreshAsync(CancellationToken cancellationToken)
	{
		if (!CanRefresh())
		{
			return;
		}

		Guid? selectedEventId = SelectedOrganizedEvent?.EventId;
		IsBusy = true;
		IsLoading = true;
		ClearContactState();
		ClearListError();
		NotifyCommandStateChanged();

		try
		{
			MyEventsResult result = await _apiService.GetMyEventsAsync(cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();

			switch (result.Status)
			{
				case MyEventsResultStatus.Success:
					ApplyMyEventsResponse(result.Response!);
					if (selectedEventId.HasValue)
					{
						OrganizedEventViewData? refreshedSelection = OrganizedEvents
							.SingleOrDefault(item => item.EventId == selectedEventId.Value);
						if (refreshedSelection is null)
						{
							ClearSelectedEvent();
						}
						else
						{
							SelectedOrganizedEvent = refreshedSelection;
							await LoadRequestQueueAsync(
								refreshedSelection.EventId,
								cancellationToken);
							await LoadOrganizerContactsAsync(
								refreshedSelection.EventId,
								cancellationToken);
						}
					}
					break;

				case MyEventsResultStatus.Unauthorized:
					SetListError("Your session has expired. Please sign in again.");
					break;

				case MyEventsResultStatus.Network:
					SetListError("Can't load your events. Check your connection and try again.");
					break;

				default:
					SetListError("Your events couldn't be loaded. Please try again.");
					break;
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		finally
		{
			IsLoading = false;
			IsRefreshing = false;
			IsBusy = false;
			NotifyScreenStateChanged();
			NotifyCommandStateChanged();
		}
	}

	[RelayCommand(CanExecute = nameof(CanOpenRequests))]
	private async Task OpenRequestsAsync(
		OrganizedEventViewData? eventItem,
		CancellationToken cancellationToken)
	{
		if (!CanOpenRequests(eventItem))
		{
			return;
		}

		OrganizedEventViewData? currentEvent = OrganizedEvents.SingleOrDefault(
			item => item.EventId == eventItem!.EventId);
		if (currentEvent is null)
		{
			return;
		}

		ClearSelectedEvent();
		SelectedOrganizedEvent = currentEvent;
		NotifyScreenStateChanged();
		NotifyCommandStateChanged();
		await LoadRequestQueueAsync(currentEvent.EventId, cancellationToken);
		await LoadOrganizerContactsAsync(currentEvent.EventId, cancellationToken);
	}

	[RelayCommand]
	private void CloseRequests()
	{
		ClearSelectedEvent();
	}

	[RelayCommand(CanExecute = nameof(CanAcceptRequest))]
	private Task AcceptRequestAsync(
		EventJoinRequestViewData? request,
		CancellationToken cancellationToken)
	{
		return ResolveRequestAsync(
			request,
			EventJoinRequestStatus.Accepted,
			requiresConfirmation: false,
			cancellationToken);
	}

	[RelayCommand(CanExecute = nameof(CanCancelEvent))]
	private async Task CancelEventAsync(
		OrganizedEventViewData? eventItem,
		CancellationToken cancellationToken)
	{
		if (!CanCancelEvent(eventItem))
		{
			return;
		}

		OrganizedEventViewData? currentEvent = OrganizedEvents.SingleOrDefault(
			item => item.EventId == eventItem!.EventId);
		if (currentEvent is null)
		{
			return;
		}

		var resolutionCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		CancellationToken resolutionToken = resolutionCancellation.Token;
		_resolutionCancellation = resolutionCancellation;
		_isConfirmationInProgress = true;
		NotifyCommandStateChanged();

		try
		{
			bool confirmed;
			try
			{
				confirmed = await _feedbackService.ShowConfirmAsync(
					"Cancel this event?",
					"Everyone who asked to join or was accepted will be notified. This cannot be undone.",
					"Confirm cancellation",
					"Cancel",
					CancellationToken.None);
			}
			finally
			{
				_isConfirmationInProgress = false;
				NotifyCommandStateChanged();
			}

			if (!confirmed)
			{
				return;
			}

			CancelEventResult result = await _apiService.CancelEventAsync(
				currentEvent.EventId,
				resolutionToken);
			resolutionToken.ThrowIfCancellationRequested();

			if (SelectedOrganizedEvent?.EventId != currentEvent.EventId
				|| !ReferenceEquals(_resolutionCancellation, resolutionCancellation))
			{
				return;
			}

			switch (result.Status)
			{
				case CancelEventResultStatus.Success:
					MoveOrganizedEventToHistory(currentEvent with
					{
						EventStatus = result.Response!.Status
					});
					ClearSelectedEvent(cancelResolution: false);
					await _feedbackService.ShowSnackbarAsync(
						"Event cancelled.",
						CancellationToken.None);
					break;

				case CancelEventResultStatus.NotFound:
					await _feedbackService.ShowSnackbarAsync(
						"This event is no longer available.",
						resolutionToken);
					await RefreshSelectedEventStateAsync(
						currentEvent.EventId,
						resolutionToken);
					break;

				case CancelEventResultStatus.Conflict:
					ApplyConflictHint(result.ConflictResponse!);
					await _feedbackService.ShowSnackbarAsync(
						ConflictMessage(result.ConflictResponse!),
						resolutionToken);
					await RefreshSelectedEventStateAsync(
						currentEvent.EventId,
						resolutionToken);
					break;

				case CancelEventResultStatus.Unauthorized:
					await _feedbackService.ShowSnackbarAsync(
						"Your session has expired. Please sign in again.",
						resolutionToken);
					break;

				case CancelEventResultStatus.Network:
					await _feedbackService.ShowSnackbarAsync(
						"The cancellation could not be confirmed. Check your connection and try again.",
						resolutionToken);
					break;

				default:
					await _feedbackService.ShowSnackbarAsync(
						"The server response could not be confirmed. Please try again.",
						resolutionToken);
					break;
			}
		}
		catch (OperationCanceledException) when (resolutionToken.IsCancellationRequested)
		{
		}
		finally
		{
			if (ReferenceEquals(_resolutionCancellation, resolutionCancellation))
			{
				_resolutionCancellation = null;
				NotifyCommandStateChanged();
			}

			resolutionCancellation.Dispose();
		}
	}

	[RelayCommand(CanExecute = nameof(CanRemoveParticipant))]
	private Task RemoveParticipantAsync(
		EventJoinRequestViewData? request,
		CancellationToken cancellationToken)
	{
		return ResolveRequestAsync(
			request,
			EventJoinRequestStatus.Removed,
			requiresConfirmation: true,
			cancellationToken);
	}

	[RelayCommand(CanExecute = nameof(CanLeaveEvent))]
	private async Task LeaveEventAsync(
		RequestedEventViewData? eventItem,
		CancellationToken cancellationToken)
	{
		if (!CanLeaveEvent(eventItem))
		{
			return;
		}

		RequestedEventViewData? currentEvent = RequestedEvents.SingleOrDefault(
			item => item.EventId == eventItem!.EventId);
		if (currentEvent is null)
		{
			return;
		}

		var resolutionCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		CancellationToken resolutionToken = resolutionCancellation.Token;
		_resolutionCancellation = resolutionCancellation;
		ReplaceRequestedEvent(
			currentEvent.EventId,
			item => item with { IsActionInFlight = true });
		_isConfirmationInProgress = true;
		NotifyCommandStateChanged();

		try
		{
			bool confirmed;
			try
			{
				confirmed = await _feedbackService.ShowConfirmAsync(
					"Leave this event?",
					"The organizer will be notified. You can ask to join again later.",
					"Leave",
					"Cancel",
					CancellationToken.None);
			}
			finally
			{
				_isConfirmationInProgress = false;
				NotifyCommandStateChanged();
			}

			if (!confirmed)
			{
				return;
			}

			ResolveJoinRequestResult result = await _apiService.LeaveEventAsync(
				currentEvent.EventId,
				resolutionToken);
			resolutionToken.ThrowIfCancellationRequested();

			if (!ReferenceEquals(_resolutionCancellation, resolutionCancellation))
			{
				return;
			}

			switch (result.Status)
			{
				case ResolveJoinRequestResultStatus.Success:
					MoveRequestedEventToHistory(currentEvent with
					{
						Status = result.Response!.Status,
						UpdatedUtc = result.Response.UpdatedUtc,
						IsActionInFlight = false,
						HasFetchedContactPayload = false,
						IsContactActionInFlight = false,
						ContactRows = []
					});
					await _feedbackService.ShowSnackbarAsync(
						"You left the event.",
						resolutionToken);
					break;

				case ResolveJoinRequestResultStatus.NotFound:
					await _feedbackService.ShowSnackbarAsync(
						"This request is no longer available.",
						resolutionToken);
					await RefreshRequestedEventStateAsync(resolutionToken);
					break;

				case ResolveJoinRequestResultStatus.Conflict:
					await _feedbackService.ShowSnackbarAsync(
						ConflictMessage(result.ConflictResponse!),
						resolutionToken);
					await RefreshRequestedEventStateAsync(resolutionToken);
					break;

				case ResolveJoinRequestResultStatus.Unauthorized:
					await _feedbackService.ShowSnackbarAsync(
						"Your session has expired. Please sign in again.",
						resolutionToken);
					break;

				case ResolveJoinRequestResultStatus.Network:
					await _feedbackService.ShowSnackbarAsync(
						"The leave could not be confirmed. Check your connection and try again.",
						resolutionToken);
					break;

				default:
					await _feedbackService.ShowSnackbarAsync(
						"The server response could not be confirmed. Please try again.",
						resolutionToken);
					break;
			}
		}
		catch (OperationCanceledException) when (resolutionToken.IsCancellationRequested)
		{
		}
		finally
		{
			if (ReferenceEquals(_resolutionCancellation, resolutionCancellation))
			{
				_resolutionCancellation = null;
				ReplaceRequestedEvent(
					currentEvent.EventId,
					item => item with { IsActionInFlight = false });
				NotifyCommandStateChanged();
			}

			resolutionCancellation.Dispose();
		}
	}

	[RelayCommand]
	private void ToggleHistory()
	{
		if (!HasHistoryEvents)
		{
			return;
		}

		IsHistorySectionExpanded = !IsHistorySectionExpanded;
	}

	[RelayCommand(CanExecute = nameof(CanRejectRequest))]
	private Task RejectRequestAsync(
		EventJoinRequestViewData? request,
		CancellationToken cancellationToken)
	{
		return ResolveRequestAsync(
			request,
			EventJoinRequestStatus.Rejected,
			requiresConfirmation: true,
			cancellationToken);
	}

	[RelayCommand(CanExecute = nameof(CanLoadRequestedContact))]
	private async Task LoadRequestedContactAsync(
		RequestedEventViewData? eventItem,
		CancellationToken cancellationToken)
	{
		if (!CanLoadRequestedContact(eventItem))
		{
			return;
		}

		RequestedEventViewData? currentEvent = RequestedEvents.SingleOrDefault(
			item => item.EventId == eventItem!.EventId);
		if (currentEvent is null)
		{
			return;
		}

		ClearContactState();
		_selectedRequestedContactEventId = currentEvent.EventId;
		ReplaceRequestedEvent(
			currentEvent.EventId,
			item => item with { IsContactActionInFlight = true });
		NotifyCommandStateChanged();

		try
		{
			(EventContactsResult result, long loadVersion) = await LoadContactsAsync(
				currentEvent.EventId,
				cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			if (loadVersion != Interlocked.Read(ref _contactLoadVersion)
				|| _selectedRequestedContactEventId != currentEvent.EventId)
			{
				return;
			}

			switch (result.Status)
			{
				case EventContactsResultStatus.Success:
					EventContactResponse? organizer = result.Response!.Contacts
						.SingleOrDefault(contact => contact.IsOrganizer);
					if (organizer is null)
					{
						await _feedbackService.ShowSnackbarAsync(
							"The organizer's contact details are unavailable.",
							cancellationToken);
						break;
					}

					_selectedRequestedContactPayload = result.Response;
					ReplaceRequestedEvent(
						currentEvent.EventId,
						item => item with
						{
							HasFetchedContactPayload = true,
							ContactRows = ProjectContactRows(organizer.Contact)
						});
					break;

				case EventContactsResultStatus.NotFound:
					await _feedbackService.ShowSnackbarAsync(
						"Contact details are not available for this request.",
						cancellationToken);
					break;

				case EventContactsResultStatus.Unauthorized:
					await _feedbackService.ShowSnackbarAsync(
						"Your session has expired. Please sign in again.",
						cancellationToken);
					break;

				case EventContactsResultStatus.Network:
					await _feedbackService.ShowSnackbarAsync(
						"Can't load contact details. Check your connection and try again.",
						cancellationToken);
					break;

				default:
					await _feedbackService.ShowSnackbarAsync(
						"Contact details couldn't be loaded. Please try again.",
						cancellationToken);
					break;
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		finally
		{
			if (_selectedRequestedContactEventId == currentEvent.EventId)
			{
				ReplaceRequestedEvent(
					currentEvent.EventId,
					item => item with { IsContactActionInFlight = false });
			}

			NotifyCommandStateChanged();
		}
	}

	[RelayCommand(CanExecute = nameof(CanOpenContact))]
	private async Task OpenContactAsync(
		ContactMethodViewData? contact,
		CancellationToken cancellationToken)
	{
		if (!CanOpenContact(contact))
		{
			return;
		}

		try
		{
			bool opened = await Launcher.Default.OpenAsync(contact!.LaunchUri!);
			if (!opened)
			{
				await _feedbackService.ShowSnackbarAsync(
					"This contact app could not be opened.",
					cancellationToken);
			}
		}
		catch (FeatureNotSupportedException)
		{
			await _feedbackService.ShowSnackbarAsync(
				"Opening this contact method is not supported on this device.",
				cancellationToken);
		}
	}

	[RelayCommand]
	private async Task CopyContactAsync(
		ContactMethodViewData? contact,
		CancellationToken cancellationToken)
	{
		if (contact is null)
		{
			return;
		}

		try
		{
			await Clipboard.Default.SetTextAsync(contact.DisplayValue);
			await _feedbackService.ShowSnackbarAsync(
				"Contact copied.",
				cancellationToken);
		}
		catch (FeatureNotSupportedException)
		{
			await _feedbackService.ShowSnackbarAsync(
				"Copying is not supported on this device.",
				cancellationToken);
		}
	}
	#endregion

	#region Public methods
	public void Cleanup()
	{
		RefreshCommand.Cancel();
		OpenRequestsCommand.Cancel();
		AcceptRequestCommand.Cancel();
		RejectRequestCommand.Cancel();
		CancelEventCommand.Cancel();
		RemoveParticipantCommand.Cancel();
		LeaveEventCommand.Cancel();
		LoadRequestedContactCommand.Cancel();
		OpenContactCommand.Cancel();
		CopyContactCommand.Cancel();
		ClearSelectedEvent();
		IsRefreshing = false;
	}
	#endregion

	#region Observable property handlers
	partial void OnSelectedOrganizedEventChanged(OrganizedEventViewData? value)
	{
		NotifyScreenStateChanged();
		NotifyCommandStateChanged();
	}

	partial void OnIsHistorySectionExpandedChanged(bool value)
	{
		OnPropertyChanged(nameof(ShowHistoryEvents));
		OnPropertyChanged(nameof(HistoryToggleLabel));
	}
	#endregion

	#region Private methods
	private async Task ResolveRequestAsync(
		EventJoinRequestViewData? request,
		EventJoinRequestStatus targetStatus,
		bool requiresConfirmation,
		CancellationToken cancellationToken)
	{
		bool canResolve = targetStatus == EventJoinRequestStatus.Accepted
			? CanAcceptRequest(request)
			: targetStatus == EventJoinRequestStatus.Removed
				? CanRemoveParticipant(request)
				: CanRejectRequest(request);
		if (!canResolve || SelectedOrganizedEvent is null)
		{
			return;
		}

		EventJoinRequestViewData? currentRequest = RequestQueue.SingleOrDefault(
			item => item.RequestId == request!.RequestId);
		if (currentRequest is null)
		{
			return;
		}

		var resolutionCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		CancellationToken resolutionToken = resolutionCancellation.Token;
		_resolutionCancellation = resolutionCancellation;
		Guid eventId = SelectedOrganizedEvent.EventId;
		ReplaceRequestRow(
			currentRequest.RequestId,
			item => item with { IsActionInFlight = true });
		NotifyCommandStateChanged();

		try
		{
			if (requiresConfirmation)
			{
				_isConfirmationInProgress = true;
				NotifyCommandStateChanged();
				bool confirmed;
				try
				{
					bool isRemoval = targetStatus == EventJoinRequestStatus.Removed;
					confirmed = await _feedbackService.ShowConfirmAsync(
						isRemoval ? "Remove this participant?" : "Reject request?",
						isRemoval
							? "They will be notified and cannot request this event again."
							: "This requester will not be able to request this event again.",
						isRemoval ? "Remove" : "Reject",
						"Cancel",
						CancellationToken.None);
				}
				finally
				{
					_isConfirmationInProgress = false;
					NotifyCommandStateChanged();
				}

				if (!confirmed)
				{
					return;
				}
			}

			ResolveJoinRequestResult result =
				targetStatus == EventJoinRequestStatus.Accepted
					? await _apiService.AcceptEventJoinRequestAsync(
						eventId,
						currentRequest.RequestId,
						resolutionToken)
					: targetStatus == EventJoinRequestStatus.Removed
						? await _apiService.RemoveEventParticipantAsync(
							eventId,
							currentRequest.RequestId,
							resolutionToken)
						: await _apiService.RejectEventJoinRequestAsync(
							eventId,
							currentRequest.RequestId,
							resolutionToken);
			resolutionToken.ThrowIfCancellationRequested();

			if (SelectedOrganizedEvent?.EventId != eventId
				|| !ReferenceEquals(_resolutionCancellation, resolutionCancellation))
			{
				return;
			}

			switch (result.Status)
			{
				case ResolveJoinRequestResultStatus.Success:
					ApplySuccessfulResolution(result.Response!);
					await _feedbackService.ShowSnackbarAsync(
						targetStatus == EventJoinRequestStatus.Accepted
							? "Join request accepted."
							: targetStatus == EventJoinRequestStatus.Removed
								? "Participant removed."
								: "Join request rejected.",
						resolutionToken);
					if (targetStatus == EventJoinRequestStatus.Accepted)
					{
						await LoadOrganizerContactsAsync(eventId, resolutionToken);
					}
					break;

				case ResolveJoinRequestResultStatus.NotFound:
					RemoveRequestRow(currentRequest.RequestId);
					await _feedbackService.ShowSnackbarAsync(
						"This request is no longer available.",
						resolutionToken);
					await RefreshSelectedEventStateAsync(eventId, resolutionToken);
					break;

				case ResolveJoinRequestResultStatus.Conflict:
					ApplyConflictHint(result.ConflictResponse!);
					await _feedbackService.ShowSnackbarAsync(
						ConflictMessage(result.ConflictResponse!),
						resolutionToken);
					await RefreshSelectedEventStateAsync(eventId, resolutionToken);
					break;

				case ResolveJoinRequestResultStatus.Unauthorized:
					await _feedbackService.ShowSnackbarAsync(
						"Your session has expired. Please sign in again.",
						resolutionToken);
					break;

				case ResolveJoinRequestResultStatus.Network:
					await _feedbackService.ShowSnackbarAsync(
						"The request result could not be confirmed. Check your connection and try again.",
						resolutionToken);
					break;

				default:
					await _feedbackService.ShowSnackbarAsync(
						"The server response could not be confirmed. Please try again.",
						resolutionToken);
					break;
			}
		}
		catch (OperationCanceledException) when (resolutionToken.IsCancellationRequested)
		{
		}
		finally
		{
			if (ReferenceEquals(_resolutionCancellation, resolutionCancellation))
			{
				_resolutionCancellation = null;
				ReplaceRequestRow(
					currentRequest.RequestId,
					item => item with { IsActionInFlight = false });
				NotifyCommandStateChanged();
			}

			resolutionCancellation.Dispose();
		}
	}

	private async Task LoadRequestQueueAsync(
		Guid eventId,
		CancellationToken cancellationToken)
	{
		CancelQueueLoad();
		var queueCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		CancellationToken queueToken = queueCancellation.Token;
		_queueCancellation = queueCancellation;
		RequestQueue.Clear();
		IsQueueLoading = true;
		ClearQueueError();
		NotifyScreenStateChanged();
		NotifyCommandStateChanged();

		try
		{
			EventJoinRequestQueueResult result =
				await _apiService.GetEventJoinRequestsAsync(eventId, queueToken);
			queueToken.ThrowIfCancellationRequested();
			if (SelectedOrganizedEvent?.EventId != eventId
				|| !ReferenceEquals(_queueCancellation, queueCancellation))
			{
				return;
			}

			switch (result.Status)
			{
				case EventJoinRequestQueueResultStatus.Success:
					UpdateRequestQueue(result.Requests!);
					break;

				case EventJoinRequestQueueResultStatus.NotFound:
					await _feedbackService.ShowSnackbarAsync(
						"This event is no longer available.",
						CancellationToken.None);
					ClearSelectedEvent();
					break;

				case EventJoinRequestQueueResultStatus.Unauthorized:
					SetQueueError("Your session has expired. Please sign in again.");
					break;

				case EventJoinRequestQueueResultStatus.Network:
					SetQueueError(
						"Can't load join requests. Check your connection and try again.");
					break;

				default:
					SetQueueError("The join requests couldn't be loaded. Please try again.");
					break;
			}
		}
		catch (OperationCanceledException) when (queueToken.IsCancellationRequested)
		{
		}
		finally
		{
			if (ReferenceEquals(_queueCancellation, queueCancellation))
			{
				_queueCancellation = null;
				IsQueueLoading = false;
				NotifyScreenStateChanged();
				NotifyCommandStateChanged();
			}

			queueCancellation.Dispose();
		}
	}

	private async Task LoadOrganizerContactsAsync(
		Guid eventId,
		CancellationToken cancellationToken)
	{
		if (SelectedOrganizedEvent is not
			{
				EventId: var selectedEventId,
				EventStatus: EventStatus.Active
			}
			|| selectedEventId != eventId)
		{
			_selectedOrganizerContactPayload = null;
			ClearOrganizerContactRows();
			return;
		}

		(EventContactsResult result, long loadVersion) = await LoadContactsAsync(
			eventId,
			cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		if (loadVersion != Interlocked.Read(ref _contactLoadVersion)
			|| SelectedOrganizedEvent?.EventId != eventId)
		{
			return;
		}

		switch (result.Status)
		{
			case EventContactsResultStatus.Success:
				_selectedOrganizerContactPayload = result.Response;
				ApplyOrganizerContacts(result.Response!);
				break;

			case EventContactsResultStatus.NotFound:
				ClearOrganizerContactRows();
				await _feedbackService.ShowSnackbarAsync(
					"Contact details are no longer available for this event.",
					cancellationToken);
				break;

			case EventContactsResultStatus.Unauthorized:
				ClearOrganizerContactRows();
				await _feedbackService.ShowSnackbarAsync(
					"Your session has expired. Please sign in again.",
					cancellationToken);
				break;

			case EventContactsResultStatus.Network:
				ClearOrganizerContactRows();
				await _feedbackService.ShowSnackbarAsync(
					"Can't load participant contacts. Refresh to try again.",
					cancellationToken);
				break;

			default:
				ClearOrganizerContactRows();
				await _feedbackService.ShowSnackbarAsync(
					"Participant contacts couldn't be loaded. Refresh to try again.",
					cancellationToken);
				break;
		}
	}

	private async Task<(EventContactsResult Result, long Version)> LoadContactsAsync(
		Guid eventId,
		CancellationToken cancellationToken)
	{
		long loadVersion = Interlocked.Increment(ref _contactLoadVersion);
		CancelContactLoad();
		var contactCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_contactCancellation = contactCancellation;

		try
		{
			EventContactsResult result = await _apiService.GetEventContactsAsync(
				eventId,
				contactCancellation.Token);
			return (result, loadVersion);
		}
		catch (OperationCanceledException)
			when (contactCancellation.IsCancellationRequested)
		{
			return (EventContactsResult.Unknown(), loadVersion);
		}
		finally
		{
			if (ReferenceEquals(_contactCancellation, contactCancellation))
			{
				_contactCancellation = null;
			}

			contactCancellation.Dispose();
		}
	}

	private async Task RefreshSelectedEventStateAsync(
		Guid eventId,
		CancellationToken cancellationToken)
	{
		MyEventsResult myEvents = await _apiService.GetMyEventsAsync(cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();

		if (myEvents.Status != MyEventsResultStatus.Success)
		{
			await _feedbackService.ShowSnackbarAsync(
				myEvents.Status == MyEventsResultStatus.Network
					? "The action was processed, but the event could not be refreshed. Check your connection and retry."
					: "The action was processed, but the event could not be refreshed.",
				cancellationToken);
			return;
		}

		ApplyMyEventsResponse(myEvents.Response!);
		OrganizedEventViewData? refreshedSelection = OrganizedEvents.SingleOrDefault(
			item => item.EventId == eventId);
		if (refreshedSelection is null)
		{
			ClearSelectedEvent();
			return;
		}

		SelectedOrganizedEvent = refreshedSelection;
		await LoadRequestQueueAsync(eventId, cancellationToken);
		await LoadOrganizerContactsAsync(eventId, cancellationToken);
	}

	private async Task RefreshRequestedEventStateAsync(
		CancellationToken cancellationToken)
	{
		MyEventsResult myEvents = await _apiService.GetMyEventsAsync(cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();

		if (myEvents.Status == MyEventsResultStatus.Success)
		{
			ClearContactState();
			ApplyMyEventsResponse(myEvents.Response!);
			return;
		}

		await _feedbackService.ShowSnackbarAsync(
			myEvents.Status == MyEventsResultStatus.Network
				? "The action was processed, but your events could not be refreshed. Check your connection and retry."
				: "The action was processed, but your events could not be refreshed.",
			cancellationToken);
	}

	private void ApplyMyEventsResponse(MyEventsResponse response)
	{
		IReadOnlyList<OrganizedEventViewData> organizedEvents = response.OrganizedEvents
			.Select(OrganizedEventViewData.FromResponse)
			.ToList();
		IReadOnlyList<RequestedEventViewData> requestedEvents = response.RequestedEvents
			.Select(RequestedEventViewData.FromResponse)
			.ToList();
		UpdateCollection(
			OrganizedEvents,
			organizedEvents.Where(item => !item.IsHistory));
		UpdateCollection(
			RequestedEvents,
			requestedEvents.Where(item => !item.IsHistory));
		UpdateCollection(
			HistoryEvents,
			organizedEvents
				.Where(item => item.IsHistory)
				.Select(HistoryEventViewData.FromOrganized)
				.Concat(
					requestedEvents
						.Where(item => item.IsHistory)
						.Select(HistoryEventViewData.FromRequested))
				.OrderByDescending(item => item.StartsAtUtc));
		NotifyScreenStateChanged();
	}

	private void ApplySuccessfulResolution(JoinRequestResponse response)
	{
		if (SelectedOrganizedEvent is null)
		{
			return;
		}

		EventJoinRequestViewData? currentRequest = RequestQueue.SingleOrDefault(
			item => item.RequestId == response.RequestId);
		if (currentRequest is null)
		{
			return;
		}

		bool wasPending = currentRequest.Status == EventJoinRequestStatus.Pending;
		int acceptedDelta =
			wasPending && response.Status == EventJoinRequestStatus.Accepted
				? 1
				: currentRequest.Status == EventJoinRequestStatus.Accepted
					&& response.Status != EventJoinRequestStatus.Accepted
					? -1
					: 0;
		int pendingDelta = wasPending ? -1 : 0;
		OrganizedEventViewData updatedEvent = SelectedOrganizedEvent with
		{
			ParticipantCount = Math.Min(
				SelectedOrganizedEvent.ParticipantCount + acceptedDelta,
				SelectedOrganizedEvent.ParticipantLimit),
			PendingRequestCount = Math.Max(
				0,
				SelectedOrganizedEvent.PendingRequestCount + pendingDelta)
		};
		ReplaceOrganizedEvent(updatedEvent);
		bool canAccept = updatedEvent.EventStatus == EventStatus.Active
			&& !updatedEvent.IsEnded
			&& !updatedEvent.IsFull;

		if (response.Status == EventJoinRequestStatus.Removed
			&& _selectedOrganizerContactPayload is not null)
		{
			_selectedOrganizerContactPayload = new EventContactsResponse(
				_selectedOrganizerContactPayload.Contacts
					.Where(contact => contact.JoinRequestId != response.RequestId)
					.ToList());
		}

		ReplaceRequestRow(
			response.RequestId,
			item => item with
			{
				Status = response.Status,
				UpdatedUtc = response.UpdatedUtc,
				AcceptAllowedByEvent = canAccept,
				IsActionInFlight = false,
				HasFetchedContactPayload =
					response.Status == EventJoinRequestStatus.Accepted
						&& item.HasFetchedContactPayload,
				ContactRows = response.Status == EventJoinRequestStatus.Accepted
					? item.ContactRows
					: []
			});
		SetAcceptAvailability(canAccept);
		NotifyScreenStateChanged();
	}

	private void ApplyOrganizerContacts(EventContactsResponse response)
	{
		IReadOnlyDictionary<Guid, EventContactResponse> contactsByRequestId =
			response.Contacts
				.Where(contact => !contact.IsOrganizer && contact.JoinRequestId.HasValue)
				.GroupBy(contact => contact.JoinRequestId!.Value)
				.ToDictionary(group => group.Key, group => group.First());

		for (int index = 0; index < RequestQueue.Count; index++)
		{
			EventJoinRequestViewData request = RequestQueue[index];
			EventContactResponse? contact = null;
			bool hasContact = request.IsContactAllowed
				&& request.Status == EventJoinRequestStatus.Accepted
				&& contactsByRequestId.TryGetValue(
					request.RequestId,
					out contact);
			RequestQueue[index] = request with
			{
				HasFetchedContactPayload = hasContact,
				ContactRows = hasContact
					? ProjectContactRows(contact!.Contact)
					: []
			};
		}
	}

	private void ApplyConflictHint(EventConflictResponse conflict)
	{
		if (SelectedOrganizedEvent is null)
		{
			return;
		}

		OrganizedEventViewData updatedEvent = conflict.Code switch
		{
			EventConflictCodes.EventFull => SelectedOrganizedEvent with
			{
				ParticipantCount = SelectedOrganizedEvent.ParticipantLimit
			},
			EventConflictCodes.EventEnded => SelectedOrganizedEvent with
			{
				IsEndedByServer = true
			},
			EventConflictCodes.EventCancelled => SelectedOrganizedEvent with
			{
				EventStatus = EventStatus.Cancelled
			},
			_ => SelectedOrganizedEvent
		};

		if (!ReferenceEquals(updatedEvent, SelectedOrganizedEvent))
		{
			ReplaceOrganizedEvent(updatedEvent);
		}

		if (conflict.Code is EventConflictCodes.EventFull
			or EventConflictCodes.EventEnded
			or EventConflictCodes.EventCancelled)
		{
			SetAcceptAvailability(false);
		}

		if (conflict.Code == EventConflictCodes.EventCancelled)
		{
			SetRequestEventState(EventStatus.Cancelled, isEnded: false);
			_selectedOrganizerContactPayload = null;
			ClearOrganizerContactRows();
		}
		else if (conflict.Code == EventConflictCodes.EventEnded)
		{
			SetRequestEventState(SelectedOrganizedEvent.EventStatus, isEnded: true);
		}
	}

	private void ReplaceOrganizedEvent(OrganizedEventViewData updatedEvent)
	{
		int index = FindIndex(
			OrganizedEvents,
			item => item.EventId == updatedEvent.EventId);
		if (index >= 0)
		{
			OrganizedEvents[index] = updatedEvent;
		}

		if (SelectedOrganizedEvent?.EventId == updatedEvent.EventId)
		{
			SelectedOrganizedEvent = updatedEvent;
		}
	}

	private void MoveOrganizedEventToHistory(OrganizedEventViewData updatedEvent)
	{
		int index = FindIndex(
			OrganizedEvents,
			item => item.EventId == updatedEvent.EventId);
		if (index >= 0)
		{
			OrganizedEvents.RemoveAt(index);
		}

		UpsertHistoryEvent(HistoryEventViewData.FromOrganized(updatedEvent));
		NotifyScreenStateChanged();
	}

	private void MoveRequestedEventToHistory(RequestedEventViewData updatedEvent)
	{
		int index = FindIndex(
			RequestedEvents,
			item => item.EventId == updatedEvent.EventId);
		if (index >= 0)
		{
			RequestedEvents.RemoveAt(index);
		}

		if (_selectedRequestedContactEventId == updatedEvent.EventId)
		{
			_selectedRequestedContactEventId = null;
			_selectedRequestedContactPayload = null;
		}

		UpsertHistoryEvent(HistoryEventViewData.FromRequested(updatedEvent));
		NotifyScreenStateChanged();
	}

	private void UpsertHistoryEvent(HistoryEventViewData updatedEvent)
	{
		UpdateCollection(
			HistoryEvents,
			HistoryEvents
				.Where(item =>
					item.EventId != updatedEvent.EventId
					|| item.IsOrganized != updatedEvent.IsOrganized)
				.Append(updatedEvent)
				.OrderByDescending(item => item.StartsAtUtc));
	}

	private void SetAcceptAvailability(bool canAccept)
	{
		for (int index = 0; index < RequestQueue.Count; index++)
		{
			RequestQueue[index] = RequestQueue[index] with
			{
				AcceptAllowedByEvent = canAccept
			};
		}

		NotifyCommandStateChanged();
	}

	private void SetRequestEventState(EventStatus eventStatus, bool isEnded)
	{
		for (int index = 0; index < RequestQueue.Count; index++)
		{
			RequestQueue[index] = RequestQueue[index] with
			{
				EventStatus = eventStatus,
				IsEventEnded = isEnded
			};
		}

		NotifyCommandStateChanged();
	}

	private void UpdateRequestQueue(
		IEnumerable<EventJoinRequestQueueItemResponse> responses)
	{
		OrganizedEventViewData selectedEvent = SelectedOrganizedEvent
			?? throw new InvalidOperationException(
				"An organized event must be selected before loading its request queue.");
		UpdateCollection(
			RequestQueue,
			responses.Select(response =>
				EventJoinRequestViewData.FromResponse(response, selectedEvent)));
		if (_selectedOrganizerContactPayload is not null)
		{
			ApplyOrganizerContacts(_selectedOrganizerContactPayload);
		}
		NotifyScreenStateChanged();
		NotifyCommandStateChanged();
	}

	private void ReplaceRequestRow(
		Guid requestId,
		Func<EventJoinRequestViewData, EventJoinRequestViewData> replace)
	{
		int index = FindIndex(RequestQueue, item => item.RequestId == requestId);
		if (index >= 0)
		{
			RequestQueue[index] = replace(RequestQueue[index]);
		}
	}

	private void RemoveRequestRow(Guid requestId)
	{
		int index = FindIndex(RequestQueue, item => item.RequestId == requestId);
		if (index < 0)
		{
			return;
		}

		bool wasPending = RequestQueue[index].IsPending;
		RequestQueue.RemoveAt(index);
		if (wasPending && SelectedOrganizedEvent is not null)
		{
			ReplaceOrganizedEvent(SelectedOrganizedEvent with
			{
				PendingRequestCount = Math.Max(
					0,
					SelectedOrganizedEvent.PendingRequestCount - 1)
			});
		}

		NotifyScreenStateChanged();
	}

	private void ReplaceRequestedEvent(
		Guid eventId,
		Func<RequestedEventViewData, RequestedEventViewData> replace)
	{
		int index = FindIndex(RequestedEvents, item => item.EventId == eventId);
		if (index >= 0)
		{
			RequestedEvents[index] = replace(RequestedEvents[index]);
		}
	}

	private void ClearSelectedEvent(bool cancelResolution = true)
	{
		CancelQueueLoad();
		if (cancelResolution)
		{
			CancelResolution();
		}
		ClearContactState();
		SelectedOrganizedEvent = null;
		RequestQueue.Clear();
		IsQueueLoading = false;
		ClearQueueError();
		NotifyScreenStateChanged();
		NotifyCommandStateChanged();
	}

	private void CancelQueueLoad()
	{
		CancellationTokenSource? cancellation =
			Interlocked.Exchange(ref _queueCancellation, null);
		cancellation?.Cancel();
		cancellation?.Dispose();
	}

	private void CancelResolution()
	{
		CancellationTokenSource? cancellation =
			Interlocked.Exchange(ref _resolutionCancellation, null);
		cancellation?.Cancel();
		cancellation?.Dispose();
	}

	private void CancelContactLoad()
	{
		CancellationTokenSource? cancellation =
			Interlocked.Exchange(ref _contactCancellation, null);
		cancellation?.Cancel();
		cancellation?.Dispose();
	}

	private void ClearContactState()
	{
		Interlocked.Increment(ref _contactLoadVersion);
		CancelContactLoad();
		_selectedOrganizerContactPayload = null;
		_selectedRequestedContactPayload = null;
		_selectedRequestedContactEventId = null;
		ClearOrganizerContactRows();

		for (int index = 0; index < RequestedEvents.Count; index++)
		{
			RequestedEvents[index] = RequestedEvents[index] with
			{
				HasFetchedContactPayload = false,
				IsContactActionInFlight = false,
				ContactRows = []
			};
		}
	}

	private void ClearOrganizerContactRows()
	{
		for (int index = 0; index < RequestQueue.Count; index++)
		{
			RequestQueue[index] = RequestQueue[index] with
			{
				HasFetchedContactPayload = false,
				ContactRows = []
			};
		}
	}

	private bool CanRefresh()
	{
		return !IsBusy
			&& _resolutionCancellation is null
			&& _contactCancellation is null;
	}

	private bool CanOpenRequests(OrganizedEventViewData? eventItem)
	{
		return eventItem is not null
			&& !IsBusy
			&& !IsQueueLoading
			&& _resolutionCancellation is null
			&& _contactCancellation is null
			&& OrganizedEvents.Any(item => item.EventId == eventItem.EventId);
	}

	private bool CanAcceptRequest(EventJoinRequestViewData? request)
	{
		return request is not null
			&& !IsBusy
			&& _resolutionCancellation is null
			&& SelectedOrganizedEvent is not null
			&& RequestQueue.FirstOrDefault(item => item.RequestId == request.RequestId)
				is { CanAccept: true };
	}

	private bool CanCancelEvent(OrganizedEventViewData? eventItem)
	{
		return eventItem is not null
			&& !IsBusy
			&& !_isConfirmationInProgress
			&& _resolutionCancellation is null
			&& SelectedOrganizedEvent?.EventId == eventItem.EventId
			&& OrganizedEvents.FirstOrDefault(item => item.EventId == eventItem.EventId)
				is { CanCancel: true };
	}

	private bool CanRejectRequest(EventJoinRequestViewData? request)
	{
		return request is not null
			&& !IsBusy
			&& _resolutionCancellation is null
			&& SelectedOrganizedEvent is not null
			&& RequestQueue.FirstOrDefault(item => item.RequestId == request.RequestId)
				is { CanReject: true };
	}

	private bool CanRemoveParticipant(EventJoinRequestViewData? request)
	{
		return request is not null
			&& !IsBusy
			&& !_isConfirmationInProgress
			&& _resolutionCancellation is null
			&& SelectedOrganizedEvent is not null
			&& RequestQueue.FirstOrDefault(item => item.RequestId == request.RequestId)
				is { CanRemove: true };
	}

	private bool CanLeaveEvent(RequestedEventViewData? eventItem)
	{
		return eventItem is not null
			&& !IsBusy
			&& !_isConfirmationInProgress
			&& _resolutionCancellation is null
			&& RequestedEvents.FirstOrDefault(item => item.EventId == eventItem.EventId)
				is { CanLeave: true };
	}

	private bool CanLoadRequestedContact(RequestedEventViewData? eventItem)
	{
		return eventItem is not null
			&& !IsBusy
			&& _contactCancellation is null
			&& RequestedEvents.FirstOrDefault(item => item.EventId == eventItem.EventId)
				is { CanLoadContact: true };
	}

	private static bool CanOpenContact(ContactMethodViewData? contact)
	{
		return contact?.CanLaunch == true;
	}

	private void SetListError(string message)
	{
		HasListError = true;
		ListErrorMessage = message;
	}

	private void ClearListError()
	{
		HasListError = false;
		ListErrorMessage = string.Empty;
	}

	private void SetQueueError(string message)
	{
		HasQueueError = true;
		QueueErrorMessage = message;
	}

	private void ClearQueueError()
	{
		HasQueueError = false;
		QueueErrorMessage = string.Empty;
	}

	private void NotifyScreenStateChanged()
	{
		OnPropertyChanged(nameof(IsListVisible));
		OnPropertyChanged(nameof(IsDetailVisible));
		OnPropertyChanged(nameof(HasOrganizedEvents));
		OnPropertyChanged(nameof(HasRequestedEvents));
		OnPropertyChanged(nameof(HasHistoryEvents));
		OnPropertyChanged(nameof(HasNoOrganizedEvents));
		OnPropertyChanged(nameof(HasNoRequestedEvents));
		OnPropertyChanged(nameof(ShowNoOrganizedEvents));
		OnPropertyChanged(nameof(ShowNoRequestedEvents));
		OnPropertyChanged(nameof(HasNoEvents));
		OnPropertyChanged(nameof(ShowHistoryEvents));
		OnPropertyChanged(nameof(HistoryToggleLabel));
		OnPropertyChanged(nameof(HasQueuedRequests));
		OnPropertyChanged(nameof(HasNoQueuedRequests));
		OnPropertyChanged(nameof(ShowInitialLoading));
		OnPropertyChanged(nameof(ShowListError));
		OnPropertyChanged(nameof(ShowQueueLoading));
		OnPropertyChanged(nameof(ShowQueueError));
	}

	private void NotifyCommandStateChanged()
	{
		RefreshCommand.NotifyCanExecuteChanged();
		OpenRequestsCommand.NotifyCanExecuteChanged();
		AcceptRequestCommand.NotifyCanExecuteChanged();
		RejectRequestCommand.NotifyCanExecuteChanged();
		CancelEventCommand.NotifyCanExecuteChanged();
		RemoveParticipantCommand.NotifyCanExecuteChanged();
		LeaveEventCommand.NotifyCanExecuteChanged();
		LoadRequestedContactCommand.NotifyCanExecuteChanged();
		OpenContactCommand.NotifyCanExecuteChanged();
	}

	private static IReadOnlyList<ContactMethodViewData> ProjectContactRows(
		ContactInfoResponse contact)
	{
		var rows = new List<ContactMethodViewData>(3);

		if (!string.IsNullOrWhiteSpace(contact.Phone))
		{
			string value = contact.Phone.Trim();
			rows.Add(new(
				ContactMethodKind.Phone,
				value,
				TryBuildPhoneUri(value),
				"contact_phone.png"));
		}

		if (!string.IsNullOrWhiteSpace(contact.Email))
		{
			string value = contact.Email.Trim();
			rows.Add(new(
				ContactMethodKind.Email,
				value,
				TryBuildEmailUri(value),
				"contact_email.png"));
		}

		if (!string.IsNullOrWhiteSpace(contact.CommunicatorHandle))
		{
			string value = contact.CommunicatorHandle.Trim();
			rows.Add(new(
				ContactMethodKind.Communicator,
				value,
				TryBuildCommunicatorUri(contact.CommunicatorPlatform, value),
				CommunicatorIconKey(contact.CommunicatorPlatform)));
		}

		return rows;
	}

	private static Uri? TryBuildPhoneUri(string value)
	{
		if (value.Length < 2 || value[0] != '+')
		{
			return null;
		}

		foreach (char character in value.AsSpan(1))
		{
			if (!char.IsAsciiDigit(character)
				&& character is not ' ' and not '(' and not ')' and not '-')
			{
				return null;
			}
		}

		string digits = string.Concat(value.Where(char.IsAsciiDigit));
		return digits.Length is >= 7 and <= 15
			? new Uri($"tel:+{digits}", UriKind.Absolute)
			: null;
	}

	private static Uri? TryBuildEmailUri(string value)
	{
		if (!MailAddress.TryCreate(value, out MailAddress? address)
			|| !string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		return Uri.TryCreate($"mailto:{address.Address}", UriKind.Absolute, out Uri? uri)
			? uri
			: null;
	}

	private static Uri? TryBuildCommunicatorUri(
		CommunicatorPlatform? platform,
		string value)
	{
		string normalized = value.Trim();
		if (normalized.StartsWith('@'))
		{
			normalized = normalized[1..];
		}

		if (platform == CommunicatorPlatform.WhatsApp)
		{
			string phoneValue = normalized.TrimStart('+');
			foreach (char character in phoneValue)
			{
				if (!char.IsAsciiDigit(character)
					&& character is not ' ' and not '(' and not ')' and not '-')
				{
					return null;
				}
			}

			string digits = string.Concat(phoneValue.Where(char.IsAsciiDigit));
			return digits.Length is >= 7 and <= 15
				? new Uri($"https://wa.me/{digits}", UriKind.Absolute)
				: null;
		}

		if (string.IsNullOrEmpty(normalized)
			|| normalized.Any(character =>
				!char.IsAsciiLetterOrDigit(character)
				&& character is not '.' and not '_'))
		{
			return null;
		}

		string escaped = Uri.EscapeDataString(normalized);
		return platform switch
		{
			CommunicatorPlatform.Messenger =>
				new Uri($"https://m.me/{escaped}", UriKind.Absolute),
			CommunicatorPlatform.Instagram =>
				new Uri($"https://www.instagram.com/{escaped}/", UriKind.Absolute),
			_ => null
		};
	}

	private static string CommunicatorIconKey(CommunicatorPlatform? platform)
	{
		return platform switch
		{
			CommunicatorPlatform.WhatsApp => "msg_whatsapp.png",
			CommunicatorPlatform.Messenger => "msg_messenger.png",
			CommunicatorPlatform.Instagram => "msg_instagram.png",
			_ => "msg_generic.png"
		};
	}

	private static void UpdateCollection<T>(
		ObservableCollection<T> target,
		IEnumerable<T> values)
	{
		IReadOnlyList<T> updated = values.ToList();
		int sharedCount = Math.Min(target.Count, updated.Count);

		for (int index = 0; index < sharedCount; index++)
		{
			if (!EqualityComparer<T>.Default.Equals(target[index], updated[index]))
			{
				target[index] = updated[index];
			}
		}

		while (target.Count > updated.Count)
		{
			target.RemoveAt(target.Count - 1);
		}

		for (int index = sharedCount; index < updated.Count; index++)
		{
			target.Add(updated[index]);
		}
	}

	private static int FindIndex<T>(
		IEnumerable<T> source,
		Func<T, bool> predicate)
	{
		int index = 0;
		foreach (T item in source)
		{
			if (predicate(item))
			{
				return index;
			}

			index++;
		}

		return -1;
	}

	private static string ConflictMessage(EventConflictResponse conflict)
	{
		return conflict.Code switch
		{
			EventConflictCodes.EventFull =>
				"This event is full. Pending requests remain available.",
			EventConflictCodes.EventEnded =>
				"This event has ended and can no longer accept participants.",
			EventConflictCodes.EventCancelled =>
				"This event has been cancelled.",
			EventConflictCodes.ParticipantNotAccepted =>
				"This participant is no longer accepted. The queue will be refreshed.",
			EventConflictCodes.RequestAlreadyResolved =>
				"This request was already resolved. The queue will be refreshed.",
			EventConflictCodes.RequestNotFound =>
				"This request is no longer available.",
			_ => conflict.Message
		};
	}
	#endregion
}
