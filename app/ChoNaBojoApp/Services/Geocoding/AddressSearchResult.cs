using Microsoft.Maui.Devices.Sensors;

namespace ChoNaBojo.App.Services.Geocoding;

#region AddressSearchStatus
public enum AddressSearchStatus
{
	Success,
	NotFound,
	Unavailable
}
#endregion

#region AddressSuggestion
public sealed record AddressSuggestion(string DisplayName, Location Location);
#endregion

#region AddressSearchResult
public sealed record AddressSearchResult
{
	#region Properties
	public AddressSearchStatus Status { get; }
	public IReadOnlyList<AddressSuggestion> Suggestions { get; }
	#endregion

	#region Constructors
	private AddressSearchResult(
		AddressSearchStatus status,
		IReadOnlyList<AddressSuggestion> suggestions)
	{
		Status = status;
		Suggestions = suggestions;
	}
	#endregion

	#region Public methods
	public static AddressSearchResult Success(IReadOnlyList<AddressSuggestion> suggestions)
	{
		return new(AddressSearchStatus.Success, suggestions);
	}

	public static AddressSearchResult NotFound()
	{
		return new(AddressSearchStatus.NotFound, []);
	}

	public static AddressSearchResult Unavailable()
	{
		return new(AddressSearchStatus.Unavailable, []);
	}
	#endregion
}
#endregion
