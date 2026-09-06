using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChoNaBojo.Contracts.DTOs;

namespace ChoNaBojo.App.ViewModels;

public partial class EventDetailViewModel : ViewModelBase
{
	#region Observable properties
	[ObservableProperty]
	private string title = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasDescription))]
	private string? description;

	[ObservableProperty]
	private string venueName = string.Empty;

	[ObservableProperty]
	private string venueAddress = string.Empty;

	[ObservableProperty]
	private string sportName = string.Empty;

	[ObservableProperty]
	private string startsAtDisplay = string.Empty;

	[ObservableProperty]
	private string estimatedEndsAtDisplay = string.Empty;

	[ObservableProperty]
	private string participantDisplay = string.Empty;

	[ObservableProperty]
	private string autoAcceptDisplay = string.Empty;
	#endregion

	#region Events
	public event EventHandler? CloseRequested;
	#endregion

	#region Public properties
	public bool HasDescription => !string.IsNullOrEmpty(Description);
	#endregion

	#region Public methods
	public void Prepare(CreatedEventResponse createdEvent)
	{
		ArgumentNullException.ThrowIfNull(createdEvent);

		Title = createdEvent.Title;
		Description = createdEvent.Description;
		VenueName = createdEvent.Venue.Name;
		VenueAddress = createdEvent.Venue.Address;
		SportName = createdEvent.Sport.Name;
		StartsAtDisplay = FormatLocalTime(createdEvent.StartsAtUtc);
		EstimatedEndsAtDisplay = FormatLocalTime(createdEvent.EstimatedEndsAtUtc);
		ParticipantDisplay = string.Create(
			CultureInfo.CurrentCulture,
			$"{createdEvent.ParticipantCount} / {createdEvent.ParticipantLimit}");
		AutoAcceptDisplay = createdEvent.AutoAccept
			? "Enabled"
			: "Organizer approval required";
	}
	#endregion

	#region Commands
	[RelayCommand]
	private void Close()
	{
		CloseRequested?.Invoke(this, EventArgs.Empty);
	}
	#endregion

	#region Private methods
	private static string FormatLocalTime(DateTimeOffset utcValue)
	{
		DateTimeOffset localValue = TimeZoneInfo.ConvertTime(
			utcValue,
			TimeZoneInfo.Local);
		return localValue.ToString(
			"dddd, d MMMM yyyy, HH:mm",
			CultureInfo.CurrentCulture);
	}
	#endregion
}
