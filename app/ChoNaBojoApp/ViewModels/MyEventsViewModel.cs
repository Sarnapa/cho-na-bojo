using System.Collections.ObjectModel;
using System.Globalization;
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
	bool IsEndedByServer)
{
	public bool IsEnded => IsEndedByServer || EstimatedEndsAtUtc <= DateTimeOffset.UtcNow;
	public bool IsFull => ParticipantCount >= ParticipantLimit;
	public double CardOpacity => IsEnded ? 0.6 : 1;
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
	public string StateLabel => IsEnded
		? "Ended"
		: IsFull
			? "Full"
			: "Open";
	public string OpenRequestsLabel => PendingRequestCount == 0
		? "View requests"
		: string.Create(
			CultureInfo.CurrentCulture,
			$"View requests ({PendingRequestCount})");
	public string SemanticDescription =>
		$"{Title}, {StateLabel}, {ParticipantDisplay} participants, {PendingRequestDisplay}";

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
	DateTimeOffset? UpdatedUtc)
{
	public bool IsEnded => EstimatedEndsAtUtc <= DateTimeOffset.UtcNow;
	public bool IsFull => ParticipantCount >= ParticipantLimit;
	public double CardOpacity => IsEnded ? 0.6 : 1;
	public bool HasRevealedContact => false;
	public bool IsContactLocked => !HasRevealedContact;
	public string StartsAtDisplay => MyEventsFormatting.FormatLocalDateTime(StartsAtUtc);
	public string EstimatedEndsAtDisplay => MyEventsFormatting.FormatLocalDateTime(
		EstimatedEndsAtUtc);
	public string ParticipantDisplay => string.Create(
		CultureInfo.CurrentCulture,
		$"{ParticipantCount} / {ParticipantLimit}");
	public string StatusLabel => Status switch
	{
		EventJoinRequestStatus.Pending => "Request pending",
		EventJoinRequestStatus.Accepted => "Joined",
		EventJoinRequestStatus.Rejected => "Request rejected",
		_ => "Status unavailable"
	};
	public string SemanticDescription =>
		$"{Title}, {StatusLabel}, {ParticipantDisplay} participants";

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
			response.UpdatedUtc);
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
	bool IsActionInFlight)
{
	public bool IsPending => Status == EventJoinRequestStatus.Pending;
	public bool IsResolved => !IsPending;
	public bool CanAccept => IsPending && AcceptAllowedByEvent && !IsActionInFlight;
	public bool CanReject => IsPending && !IsActionInFlight;
	public bool HasRevealedContact => false;
	public bool IsContactLocked => !HasRevealedContact;
	public string CreatedDisplay => MyEventsFormatting.FormatLocalDateTime(CreatedUtc);
	public string StatusLabel => IsActionInFlight
		? "Updating..."
		: Status switch
		{
			EventJoinRequestStatus.Pending => "Pending",
			EventJoinRequestStatus.Accepted => "Accepted",
			EventJoinRequestStatus.Rejected => "Rejected",
			_ => "Status unavailable"
		};
	public string SemanticDescription =>
		$"{RequesterDisplayKey}, requested {CreatedDisplay}, {StatusLabel}";

	public static EventJoinRequestViewData FromResponse(
		EventJoinRequestQueueItemResponse response,
		bool acceptAllowedByEvent)
	{
		return new(
			response.RequestId,
			response.RequesterDisplayKey,
			response.Status,
			response.CreatedUtc,
			response.UpdatedUtc,
			acceptAllowedByEvent,
			IsActionInFlight: false);
	}
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
	private bool _isConfirmationInProgress;
	#endregion

	#region Observable properties
	[ObservableProperty]
	private ObservableCollection<OrganizedEventViewData> organizedEvents = [];

	[ObservableProperty]
	private ObservableCollection<RequestedEventViewData> requestedEvents = [];

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
	public bool HasNoOrganizedEvents => !HasOrganizedEvents;
	public bool HasNoRequestedEvents => !HasRequestedEvents;
	public bool ShowNoOrganizedEvents => HasNoOrganizedEvents && HasRequestedEvents;
	public bool ShowNoRequestedEvents => HasNoRequestedEvents && HasOrganizedEvents;
	public bool HasNoEvents => !HasOrganizedEvents && !HasRequestedEvents;
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
	#endregion

	#region Public methods
	public void Cleanup()
	{
		RefreshCommand.Cancel();
		OpenRequestsCommand.Cancel();
		AcceptRequestCommand.Cancel();
		RejectRequestCommand.Cancel();
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
				bool confirmed;
				try
				{
					confirmed = await _feedbackService.ShowConfirmAsync(
						"Reject request?",
						"This requester will not be able to request this event again.",
						"Reject",
						"Cancel",
						CancellationToken.None);
				}
				finally
				{
					_isConfirmationInProgress = false;
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
							: "Join request rejected.",
						resolutionToken);
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
	}

	private void ApplyMyEventsResponse(MyEventsResponse response)
	{
		UpdateCollection(
			OrganizedEvents,
			response.OrganizedEvents.Select(OrganizedEventViewData.FromResponse));
		UpdateCollection(
			RequestedEvents,
			response.RequestedEvents.Select(RequestedEventViewData.FromResponse));
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
		int acceptedDelta = wasPending
			&& response.Status == EventJoinRequestStatus.Accepted
				? 1
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
		bool canAccept = !updatedEvent.IsEnded && !updatedEvent.IsFull;

		ReplaceRequestRow(
			response.RequestId,
			item => item with
			{
				Status = response.Status,
				UpdatedUtc = response.UpdatedUtc,
				AcceptAllowedByEvent = canAccept,
				IsActionInFlight = false
			});
		SetAcceptAvailability(canAccept);
		NotifyScreenStateChanged();
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
			_ => SelectedOrganizedEvent
		};

		if (!ReferenceEquals(updatedEvent, SelectedOrganizedEvent))
		{
			ReplaceOrganizedEvent(updatedEvent);
		}

		if (conflict.Code is EventConflictCodes.EventFull or EventConflictCodes.EventEnded)
		{
			SetAcceptAvailability(false);
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

	private void UpdateRequestQueue(
		IEnumerable<EventJoinRequestQueueItemResponse> responses)
	{
		bool canAccept = SelectedOrganizedEvent is
		{
			IsEnded: false,
			IsFull: false
		};
		UpdateCollection(
			RequestQueue,
			responses.Select(response =>
				EventJoinRequestViewData.FromResponse(response, canAccept)));
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

	private void ClearSelectedEvent()
	{
		CancelQueueLoad();
		CancelResolution();
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

	private bool CanRefresh()
	{
		return !IsBusy && _resolutionCancellation is null;
	}

	private bool CanOpenRequests(OrganizedEventViewData? eventItem)
	{
		return eventItem is not null
			&& !IsBusy
			&& !IsQueueLoading
			&& _resolutionCancellation is null
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

	private bool CanRejectRequest(EventJoinRequestViewData? request)
	{
		return request is not null
			&& !IsBusy
			&& _resolutionCancellation is null
			&& SelectedOrganizedEvent is not null
			&& RequestQueue.FirstOrDefault(item => item.RequestId == request.RequestId)
				is { CanReject: true };
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
		OnPropertyChanged(nameof(HasNoOrganizedEvents));
		OnPropertyChanged(nameof(HasNoRequestedEvents));
		OnPropertyChanged(nameof(ShowNoOrganizedEvents));
		OnPropertyChanged(nameof(ShowNoRequestedEvents));
		OnPropertyChanged(nameof(HasNoEvents));
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
			EventConflictCodes.RequestAlreadyResolved =>
				"This request was already resolved. The queue will be refreshed.",
			EventConflictCodes.RequestNotFound =>
				"This request is no longer available.",
			_ => conflict.Message
		};
	}
	#endregion
}
