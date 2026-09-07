using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChoNaBojo.App.Services.Events;

namespace ChoNaBojo.App.ViewModels;

public partial class AvailabilityFilterViewModel : ViewModelBase
{
	#region Private fields
	private EventAvailabilityWindow? _validatedWindow;
	private bool _isPreparing;
	#endregion

	#region Observable properties
	[ObservableProperty]
	private DateTime? startDate;

	[ObservableProperty]
	private TimeSpan? startTime;

	[ObservableProperty]
	private DateTime? endDate;

	[ObservableProperty]
	private TimeSpan? endTime;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasAvailableFromError))]
	private string availableFromError = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasAvailableToError))]
	private string availableToError = string.Empty;
	#endregion

	#region Events
	public event EventHandler<EventAvailabilityWindow>? ApplyRequested;
	public event EventHandler? ClearRequested;
	public event EventHandler? CancelRequested;
	#endregion

	#region Public properties
	public bool CanApply => _validatedWindow is not null;

	public bool HasAvailableFromError => !string.IsNullOrEmpty(AvailableFromError);

	public bool HasAvailableToError => !string.IsNullOrEmpty(AvailableToError);
	#endregion

	#region Commands
	[RelayCommand(CanExecute = nameof(CanApply))]
	private void Apply()
	{
		if (_validatedWindow is not null)
		{
			ApplyRequested?.Invoke(this, _validatedWindow);
		}
	}

	[RelayCommand]
	private void Clear()
	{
		ClearRequested?.Invoke(this, EventArgs.Empty);
	}

	[RelayCommand]
	private void Cancel()
	{
		CancelRequested?.Invoke(this, EventArgs.Empty);
	}
	#endregion

	#region Public methods
	public void Prepare(EventAvailabilityWindow? currentWindow)
	{
		DateTime localStart;
		DateTime localEnd;
		if (currentWindow is not null)
		{
			localStart = TimeZoneInfo.ConvertTime(
				currentWindow.AvailableFromUtc,
				TimeZoneInfo.Local).DateTime;
			localEnd = TimeZoneInfo.ConvertTime(
				currentWindow.AvailableToUtc,
				TimeZoneInfo.Local).DateTime;
		}
		else
		{
			DateTime now = DateTime.Now;
			localStart = new DateTime(
				now.Year,
				now.Month,
				now.Day,
				now.Hour,
				now.Minute,
				0).AddHours(1);
			localEnd = localStart.AddHours(2);
		}

		_isPreparing = true;
		StartDate = localStart.Date;
		StartTime = localStart.TimeOfDay;
		EndDate = localEnd.Date;
		EndTime = localEnd.TimeOfDay;
		_isPreparing = false;
		ValidateDraft();
	}
	#endregion

	#region Observable property handlers
	partial void OnStartDateChanged(DateTime? value)
	{
		ValidateDraftUnlessPreparing();
	}

	partial void OnStartTimeChanged(TimeSpan? value)
	{
		ValidateDraftUnlessPreparing();
	}

	partial void OnEndDateChanged(DateTime? value)
	{
		ValidateDraftUnlessPreparing();
	}

	partial void OnEndTimeChanged(TimeSpan? value)
	{
		ValidateDraftUnlessPreparing();
	}
	#endregion

	#region Private methods
	private void ValidateDraftUnlessPreparing()
	{
		if (!_isPreparing)
		{
			ValidateDraft();
		}
	}

	private void ValidateDraft()
	{
		_validatedWindow = null;
		AvailableFromError = StartDate.HasValue && StartTime.HasValue
			? string.Empty
			: "Choose an availability start date and time.";
		AvailableToError = EndDate.HasValue && EndTime.HasValue
			? string.Empty
			: "Choose an availability end date and time.";

		if (!StartDate.HasValue
			|| !StartTime.HasValue
			|| !EndDate.HasValue
			|| !EndTime.HasValue)
		{
			NotifyValidationChanged();
			return;
		}

		EventAvailabilityConversionResult conversion =
			EventAvailabilityConversion.ConvertCustomToUtc(
				StartDate.Value,
				StartTime.Value,
				EndDate.Value,
				EndTime.Value);
		AvailableFromError = GetFirstError(
			conversion.Errors,
			"availableFromUtc");
		AvailableToError = GetFirstError(
			conversion.Errors,
			"availableToUtc");
		_validatedWindow = conversion.Window;
		NotifyValidationChanged();
	}

	private void NotifyValidationChanged()
	{
		OnPropertyChanged(nameof(CanApply));
		ApplyCommand.NotifyCanExecuteChanged();
	}

	private static string GetFirstError(
		IReadOnlyDictionary<string, string[]> errors,
		string field)
	{
		return errors.TryGetValue(field, out string[]? messages)
			? messages.FirstOrDefault() ?? string.Empty
			: string.Empty;
	}
	#endregion
}
