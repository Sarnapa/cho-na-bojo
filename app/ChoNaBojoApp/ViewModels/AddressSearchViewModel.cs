using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChoNaBojo.App.Services.Feedback;
using ChoNaBojo.App.Services.Geocoding;

namespace ChoNaBojo.App.ViewModels;

public partial class AddressSearchViewModel : ViewModelBase
{
	#region Private constants
	private const int MinimumQueryLength = 2;
	#endregion

	#region Private static fields
	private static readonly TimeSpan SuggestionDelay = TimeSpan.FromMilliseconds(350);
	#endregion

	#region Private fields
	private readonly IAddressSearchService _addressSearchService;
	private readonly IFeedbackService _feedbackService;
	private CancellationTokenSource? _sessionCancellation;
	private CancellationTokenSource? _suggestionSearchCancellation;
	#endregion

	#region Observable properties
	[ObservableProperty]
	private string query = string.Empty;

	[ObservableProperty]
	private IReadOnlyList<AddressSuggestion> suggestions = [];

	[ObservableProperty]
	private bool isSearching;

	[ObservableProperty]
	private bool hasStatusMessage;

	[ObservableProperty]
	private string statusMessage = string.Empty;
	#endregion

	#region Constructors
	public AddressSearchViewModel(
		IAddressSearchService addressSearchService,
		IFeedbackService feedbackService)
	{
		_addressSearchService = addressSearchService;
		_feedbackService = feedbackService;
	}
	#endregion

	#region Events
	public event EventHandler<AddressSuggestion>? AddressSelected;
	public event EventHandler? CancelRequested;
	#endregion

	#region Commands
	[RelayCommand]
	private async Task SubmitAsync()
	{
		CancelSuggestionSearch();
		if (string.IsNullOrWhiteSpace(Query))
		{
			await _feedbackService.ShowSnackbarAsync("Enter an address to search");
			return;
		}

		CancellationToken cancellationToken = _sessionCancellation?.Token
			?? throw new InvalidOperationException("Address search is not active.");
		IsBusy = true;
		IsSearching = true;
		ClearStatus();
		try
		{
			AddressSearchResult result =
				await _addressSearchService.SearchAsync(Query, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			if (result.Status == AddressSearchStatus.Success)
			{
				AddressSelected?.Invoke(this, result.Suggestions[0]);
				return;
			}

			await ShowFailureAsync(result.Status, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		finally
		{
			IsSearching = false;
			IsBusy = false;
		}
	}

	[RelayCommand]
	private void SelectSuggestion(AddressSuggestion suggestion)
	{
		CancelSuggestionSearch();
		AddressSelected?.Invoke(this, suggestion);
	}

	[RelayCommand]
	private void Cancel()
	{
		Stop();
		CancelRequested?.Invoke(this, EventArgs.Empty);
	}
	#endregion

	#region Public methods
	public void Prepare(string initialQuery)
	{
		Stop();
		_sessionCancellation = new CancellationTokenSource();
		Suggestions = [];
		ClearStatus();
		Query = initialQuery;
	}

	public void Stop()
	{
		CancelSuggestionSearch();
		CancellationTokenSource? sessionCancellation =
			Interlocked.Exchange(ref _sessionCancellation, null);
		sessionCancellation?.Cancel();
		sessionCancellation?.Dispose();
		IsSearching = false;
		IsBusy = false;
	}
	#endregion

	#region Observable property handlers
	partial void OnQueryChanged(string value)
	{
		QueueSuggestionSearch(value);
	}
	#endregion

	#region Private methods
	private void QueueSuggestionSearch(string value)
	{
		CancelSuggestionSearch();
		Suggestions = [];
		ClearStatus();

		string queryValue = value.Trim();
		if (queryValue.Length < MinimumQueryLength)
		{
			return;
		}

		CancellationToken sessionToken = _sessionCancellation?.Token
			?? throw new InvalidOperationException("Address search is not active.");
		var cancellation =
			CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
		_suggestionSearchCancellation = cancellation;
		_ = LoadSuggestionsAsync(queryValue, cancellation);
	}

	private async Task LoadSuggestionsAsync(
		string queryValue,
		CancellationTokenSource cancellation)
	{
		try
		{
			await Task.Delay(SuggestionDelay, cancellation.Token);
			if (!ReferenceEquals(_suggestionSearchCancellation, cancellation))
			{
				return;
			}

			IsSearching = true;
			AddressSearchResult result =
				await _addressSearchService.SearchAsync(queryValue, cancellation.Token);
			cancellation.Token.ThrowIfCancellationRequested();
			if (!ReferenceEquals(_suggestionSearchCancellation, cancellation))
			{
				return;
			}

			Suggestions = result.Suggestions;
			SetStatus(result.Status);
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		finally
		{
			if (ReferenceEquals(_suggestionSearchCancellation, cancellation))
			{
				_suggestionSearchCancellation = null;
				IsSearching = false;
			}

			cancellation.Dispose();
		}
	}

	private void CancelSuggestionSearch()
	{
		CancellationTokenSource? cancellation =
			Interlocked.Exchange(ref _suggestionSearchCancellation, null);
		cancellation?.Cancel();
		if (cancellation is not null)
		{
			IsSearching = false;
		}
	}

	private void SetStatus(AddressSearchStatus status)
	{
		switch (status)
		{
			case AddressSearchStatus.Success:
				ClearStatus();
				break;
			case AddressSearchStatus.NotFound:
				StatusMessage = "No matching addresses found";
				HasStatusMessage = true;
				break;
			case AddressSearchStatus.Unavailable:
				StatusMessage = "Address search isn't available on this device";
				HasStatusMessage = true;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(status), status, null);
		}
	}

	private async Task ShowFailureAsync(
		AddressSearchStatus status,
		CancellationToken cancellationToken)
	{
		SetStatus(status);
		await _feedbackService.ShowSnackbarAsync(StatusMessage, cancellationToken);
	}

	private void ClearStatus()
	{
		StatusMessage = string.Empty;
		HasStatusMessage = false;
	}
	#endregion
}
