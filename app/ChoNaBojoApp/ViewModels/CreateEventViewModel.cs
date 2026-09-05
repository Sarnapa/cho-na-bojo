using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChoNaBojo.App.Services;
using ChoNaBojo.App.Services.Events;
using ChoNaBojo.App.Services.Venues;
using ChoNaBojo.Contracts.Consts;
using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Validation;

namespace ChoNaBojo.App.ViewModels;

#region CreateEventSportOption
public sealed record CreateEventSportOption(int Id, string Name)
{
	public override string ToString()
	{
		return Name;
	}
}
#endregion

#region EventCreatedEventArgs
public sealed class EventCreatedEventArgs : EventArgs
{
	public CreatedEventResponse Response { get; }
	public bool IsReplay { get; }

	public EventCreatedEventArgs(CreatedEventResponse response, bool isReplay)
	{
		Response = response;
		IsReplay = isReplay;
	}
}
#endregion

#region VenueInvalidatedEventArgs
public sealed class VenueInvalidatedEventArgs : EventArgs
{
	public string Message { get; }

	public VenueInvalidatedEventArgs(string message)
	{
		Message = message;
	}
}
#endregion

public partial class CreateEventViewModel : ViewModelBase
{
	#region Private static fields
	private static readonly TimeSpan LongWaitThreshold = TimeSpan.FromSeconds(5);
	#endregion

	#region Private fields
	private readonly IApiService _apiService;
	private readonly IVenueCatalog _venueCatalog;
	private VenueResponse? _venue;
	private Guid _clientRequestId;
	private CreateEventRequest? _pendingRequest;
	#endregion

	#region Observable properties
	[ObservableProperty]
	private string venueName = string.Empty;

	[ObservableProperty]
	private string venueAddress = string.Empty;

	[ObservableProperty]
	private IReadOnlyList<CreateEventSportOption> supportedSports = [];

	[ObservableProperty]
	private CreateEventSportOption? selectedSport;

	[ObservableProperty]
	private string title = string.Empty;

	[ObservableProperty]
	private string description = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EndDateMinimum))]
	private DateTime startDate;

	[ObservableProperty]
	private DateTime endDate;

	[ObservableProperty]
	private TimeSpan startTime;

	[ObservableProperty]
	private TimeSpan endTime;

	[ObservableProperty]
	private string participantLimitText = EventPolicy.ParticipantLimitMinimum.ToString(
		CultureInfo.InvariantCulture);

	[ObservableProperty]
	private bool autoAccept;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanEdit))]
	[NotifyPropertyChangedFor(nameof(CanCancel))]
	private bool isRequestInFlight;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanEdit))]
	[NotifyPropertyChangedFor(nameof(IsRetryVisible))]
	[NotifyPropertyChangedFor(nameof(IsCreateVisible))]
	private bool hasPendingRetry;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(WaitMessage))]
	private bool isLongWait;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasTitleError))]
	private string? titleError;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasDescriptionError))]
	private string? descriptionError;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasSportError))]
	private string? sportError;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasStartTimeError))]
	private string? startTimeError;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasEndTimeError))]
	private string? endTimeError;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasParticipantLimitError))]
	private string? participantLimitError;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasGeneralError))]
	private string? generalError;
	#endregion

	#region Constructors
	public CreateEventViewModel(IApiService apiService, IVenueCatalog venueCatalog)
	{
		_apiService = apiService;
		_venueCatalog = venueCatalog;
	}
	#endregion

	#region Events
	public event EventHandler<EventCreatedEventArgs>? EventCreated;
	public event EventHandler? CancelRequested;
	public event EventHandler? SportSelectionRequested;
	public event EventHandler<VenueInvalidatedEventArgs>? VenueInvalidated;
	public event EventHandler? RequestSettled;
	#endregion

	#region Public properties
	public DateTime MinimumDate => DateTime.Today;
	public DateTime EndDateMinimum => StartDate;
	public bool CanEdit => !IsRequestInFlight && !HasPendingRetry;
	public bool CanCancel => !IsRequestInFlight;
	public bool IsRetryVisible => HasPendingRetry;
	public bool IsCreateVisible => !HasPendingRetry;
	public string WaitMessage => IsLongWait
		? "Still creating... This is taking longer than usual."
		: "Creating event...";
	public bool CatalogWasRefreshed { get; private set; }
	public bool HasTitleError => !string.IsNullOrEmpty(TitleError);
	public bool HasDescriptionError => !string.IsNullOrEmpty(DescriptionError);
	public bool HasSportError => !string.IsNullOrEmpty(SportError);
	public bool HasStartTimeError => !string.IsNullOrEmpty(StartTimeError);
	public bool HasEndTimeError => !string.IsNullOrEmpty(EndTimeError);
	public bool HasParticipantLimitError => !string.IsNullOrEmpty(ParticipantLimitError);
	public bool HasGeneralError => !string.IsNullOrEmpty(GeneralError);
	#endregion

	#region Public methods
	public void Prepare(VenueResponse venue, int? activeSportId)
	{
		ArgumentNullException.ThrowIfNull(venue);
		if (IsRequestInFlight)
		{
			throw new InvalidOperationException(
				"Cannot replace an event draft while a create request is in flight.");
		}

		_venue = venue;
		_clientRequestId = Guid.NewGuid();
		_pendingRequest = null;
		CatalogWasRefreshed = false;
		IsBusy = false;
		IsRequestInFlight = false;
		HasPendingRetry = false;
		IsLongWait = false;

		VenueName = venue.Name;
		VenueAddress = venue.Address;
		Title = string.Empty;
		Description = string.Empty;
		ParticipantLimitText = EventPolicy.ParticipantLimitMinimum.ToString(
			CultureInfo.InvariantCulture);
		AutoAccept = false;

		DateTime suggestedStart = DateTime.Now.AddHours(1);
		DateTime suggestedEnd = suggestedStart.AddHours(1);
		StartDate = suggestedStart.Date;
		StartTime = new TimeSpan(suggestedStart.Hour, suggestedStart.Minute, 0);
		EndDate = suggestedEnd.Date;
		EndTime = suggestedEnd.TimeOfDay;

		RebuildSupportedSports(activeSportId);
		ClearErrors();
		if (SupportedSports.Count == 0)
		{
			SportError = "This venue currently has no supported sports.";
		}
	}
	#endregion

	#region Commands
	[RelayCommand]
	private async Task CreateAsync()
	{
		if (!CanEdit)
		{
			return;
		}

		ClearErrors();
		CreateEventRequest? request = BuildRequest();
		if (request is null)
		{
			return;
		}

		_pendingRequest = request;
		await SubmitAsync(request);
	}

	[RelayCommand]
	private Task RetryAsync()
	{
		return !HasPendingRetry || _pendingRequest is null
			? Task.CompletedTask
			: SubmitAsync(_pendingRequest);
	}

	[RelayCommand]
	private void Cancel()
	{
		if (!CanCancel)
		{
			return;
		}

		CancelRequested?.Invoke(this, EventArgs.Empty);
	}
	#endregion

	#region Observable property handlers
	partial void OnSelectedSportChanged(CreateEventSportOption? value)
	{
		if (CanEdit && value is not null)
		{
			SportError = null;
		}
	}

	partial void OnTitleChanged(string value)
	{
		if (CanEdit)
		{
			TitleError = null;
		}
	}

	partial void OnDescriptionChanged(string value)
	{
		if (CanEdit)
		{
			DescriptionError = null;
		}
	}

	partial void OnStartDateChanged(DateTime value)
	{
		if (EndDate < value.Date)
		{
			EndDate = value.Date;
		}

		if (CanEdit)
		{
			StartTimeError = null;
			EndTimeError = null;
		}
	}

	partial void OnEndDateChanged(DateTime value)
	{
		if (CanEdit)
		{
			EndTimeError = null;
		}
	}

	partial void OnStartTimeChanged(TimeSpan value)
	{
		if (CanEdit)
		{
			StartTimeError = null;
			EndTimeError = null;
		}
	}

	partial void OnEndTimeChanged(TimeSpan value)
	{
		if (CanEdit)
		{
			EndTimeError = null;
		}
	}

	partial void OnParticipantLimitTextChanged(string value)
	{
		if (CanEdit)
		{
			ParticipantLimitError = null;
		}
	}
	#endregion

	#region Private methods
	private CreateEventRequest? BuildRequest()
	{
		if (_venue is null)
		{
			throw new InvalidOperationException("Prepare must be called before creating an event.");
		}

		if (SelectedSport is null)
		{
			SportError = "Choose a sport.";
		}

		if (!int.TryParse(
			ParticipantLimitText,
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out int participantLimit))
		{
			ParticipantLimitError = "Participant limit must be a whole number.";
		}

		EventTimeConversionResult timeResult = EventTimeConversion.ConvertToUtc(
			StartDate,
			StartTime,
			EndDate,
			EndTime);
		if (!timeResult.IsValid)
		{
			ApplyValidationErrors(timeResult.Errors);
		}

		if (SelectedSport is null
			|| ParticipantLimitError is not null
			|| !timeResult.IsValid)
		{
			return null;
		}

		var request = new CreateEventRequest(
			_clientRequestId,
			_venue.Id,
			SelectedSport.Id,
			Title.Trim(),
			NormalizeOptionalText(Description),
			timeResult.StartsAtUtc!.Value,
			timeResult.EstimatedEndsAtUtc!.Value,
			participantLimit,
			AutoAccept);

		ValidationResult validation = EventValidation.ValidateCreateEventRequest(
			request,
			DateTimeOffset.UtcNow);
		if (!validation.IsValid)
		{
			ApplyValidationErrors(validation.Errors);
			return null;
		}

		return request;
	}

	private async Task SubmitAsync(CreateEventRequest request)
	{
		if (IsRequestInFlight)
		{
			return;
		}

		EventCreatedEventArgs? createdEvent = null;
		VenueInvalidatedEventArgs? invalidatedVenue = null;
		bool requestSportSelection = false;

		GeneralError = null;
		HasPendingRetry = false;
		IsLongWait = false;
		IsRequestInFlight = true;
		IsBusy = true;

		using var longWaitCancellation = new CancellationTokenSource();
		Task longWaitTask = ShowLongWaitAsync(longWaitCancellation.Token);
		try
		{
			CreateEventResult result = await _apiService.CreateEventAsync(
				request,
				CancellationToken.None);
			switch (result.Status)
			{
				case CreateEventResultStatus.Success:
					_pendingRequest = null;
					createdEvent = new EventCreatedEventArgs(
						result.Response!,
						result.IsReplay);
					break;

				case CreateEventResultStatus.ValidationFailed:
					_pendingRequest = null;
					ApplyValidationErrors(result.ValidationErrors!);
					break;

				case CreateEventResultStatus.ReferenceChanged:
					_pendingRequest = null;
					(invalidatedVenue, requestSportSelection) =
						await RecoverFromReferenceChangeAsync(result.Conflict!);
					break;

				case CreateEventResultStatus.Unauthorized:
					_pendingRequest = null;
					GeneralError = "Your session has expired. Please sign in again.";
					break;

				case CreateEventResultStatus.Network:
					HasPendingRetry = true;
					GeneralError =
						"The result is uncertain because the server could not be reached. Retry sends the exact same request safely.";
					break;

				default:
					HasPendingRetry = true;
					GeneralError =
						"The server response could not be confirmed. Retry sends the exact same request safely.";
					break;
			}
		}
		finally
		{
			longWaitCancellation.Cancel();
			await longWaitTask;
			IsLongWait = false;
			IsRequestInFlight = false;
			IsBusy = false;
			RequestSettled?.Invoke(this, EventArgs.Empty);
		}

		if (createdEvent is not null)
		{
			EventCreated?.Invoke(this, createdEvent);
		}
		else if (invalidatedVenue is not null)
		{
			VenueInvalidated?.Invoke(this, invalidatedVenue);
		}
		else if (requestSportSelection)
		{
			SportSelectionRequested?.Invoke(this, EventArgs.Empty);
		}
	}

	private async Task<(VenueInvalidatedEventArgs? Venue, bool SelectSport)>
		RecoverFromReferenceChangeAsync(EventConflictResponse conflict)
	{
		VenueCatalogLoadResult refreshResult = await _venueCatalog.RefreshAsync(
			CancellationToken.None);
		CatalogWasRefreshed = refreshResult.Status == VenueCatalogLoadStatus.Success;

		bool venueConflict = string.Equals(
				conflict.Code,
				"venue_not_found",
				StringComparison.Ordinal)
			|| string.Equals(conflict.Field, "venueId", StringComparison.Ordinal);
		if (venueConflict)
		{
			string message = refreshResult.Status == VenueCatalogLoadStatus.Success
				? $"{conflict.Message} Choose another venue from the refreshed map."
				: $"{conflict.Message} The venue catalog could not be refreshed.";
			return (new VenueInvalidatedEventArgs(message), false);
		}

		if (refreshResult.Status == VenueCatalogLoadStatus.Success)
		{
			VenueResponse? refreshedVenue = _venueCatalog.Venues.SingleOrDefault(
				venue => venue.Id == _venue!.Id);
			if (refreshedVenue is null)
			{
				return (
					new VenueInvalidatedEventArgs(
						"The selected venue no longer exists. Choose another venue from the refreshed map."),
					false);
			}

			_venue = refreshedVenue;
			VenueName = refreshedVenue.Name;
			VenueAddress = refreshedVenue.Address;
			RebuildSupportedSports(activeSportId: null);
		}

		SelectedSport = null;
		SportError = conflict.Message;
		if (refreshResult.Status != VenueCatalogLoadStatus.Success)
		{
			GeneralError = refreshResult.Status switch
			{
				VenueCatalogLoadStatus.Unauthorized =>
					"Your session expired while refreshing the sport list.",
				VenueCatalogLoadStatus.Network =>
					"The sport list could not be refreshed. Check your connection and try again.",
				_ => "The sport list could not be refreshed. Try again."
			};
		}

		return (null, true);
	}

	private void RebuildSupportedSports(int? activeSportId)
	{
		if (_venue is null)
		{
			SupportedSports = [];
			SelectedSport = null;
			return;
		}

		IReadOnlyDictionary<int, SportResponse> sportsById = _venueCatalog.Sports
			.ToDictionary(sport => sport.Id);
		SupportedSports = _venue.SportIds
			.Where(sportsById.ContainsKey)
			.Select(sportId => new CreateEventSportOption(
				sportId,
				sportsById[sportId].Name))
			.ToList();

		SelectedSport = activeSportId is not null
			? SupportedSports.FirstOrDefault(sport => sport.Id == activeSportId.Value)
			: null;
		if (SelectedSport is null && SupportedSports.Count == 1)
		{
			SelectedSport = SupportedSports[0];
		}
	}

	private void ApplyValidationErrors(IReadOnlyDictionary<string, string[]> errors)
	{
		bool appliedAnyError = false;
		if (errors.ContainsKey("title"))
		{
			TitleError = GetErrors(errors, "title");
			appliedAnyError = true;
		}

		if (errors.ContainsKey("description"))
		{
			DescriptionError = GetErrors(errors, "description");
			appliedAnyError = true;
		}

		if (errors.ContainsKey("sportId"))
		{
			SportError = GetErrors(errors, "sportId");
			appliedAnyError = true;
		}

		if (errors.ContainsKey("startsAtUtc"))
		{
			StartTimeError = GetErrors(errors, "startsAtUtc");
			appliedAnyError = true;
		}

		if (errors.ContainsKey("estimatedEndsAtUtc"))
		{
			EndTimeError = GetErrors(errors, "estimatedEndsAtUtc");
			appliedAnyError = true;
		}

		if (errors.ContainsKey("participantLimit"))
		{
			ParticipantLimitError = GetErrors(errors, "participantLimit");
			appliedAnyError = true;
		}

		var generalMessages = new List<string>();
		AddErrors(generalMessages, errors, "clientRequestId");
		AddErrors(generalMessages, errors, "venueId");
		foreach (KeyValuePair<string, string[]> error in errors)
		{
			if (error.Key is "title"
				or "description"
				or "sportId"
				or "startsAtUtc"
				or "estimatedEndsAtUtc"
				or "participantLimit"
				or "clientRequestId"
				or "venueId")
			{
				continue;
			}

			generalMessages.AddRange(error.Value);
		}

		if (generalMessages.Count > 0)
		{
			GeneralError = string.Join(" ", generalMessages);
		}
		else if (!appliedAnyError)
		{
			GeneralError = "The event details could not be validated. Review the form and try again.";
		}
	}

	private void ClearErrors()
	{
		TitleError = null;
		DescriptionError = null;
		SportError = null;
		StartTimeError = null;
		EndTimeError = null;
		ParticipantLimitError = null;
		GeneralError = null;
	}

	private static string? GetErrors(
		IReadOnlyDictionary<string, string[]> errors,
		string field)
	{
		return errors.TryGetValue(field, out string[]? messages)
			? string.Join(" ", messages)
			: null;
	}

	private static void AddErrors(
		ICollection<string> destination,
		IReadOnlyDictionary<string, string[]> source,
		string field)
	{
		if (source.TryGetValue(field, out string[]? messages))
		{
			foreach (string message in messages)
			{
				destination.Add(message);
			}
		}
	}

	private static string? NormalizeOptionalText(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}

	private async Task ShowLongWaitAsync(CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(LongWaitThreshold, cancellationToken);
			IsLongWait = true;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}
	#endregion
}
