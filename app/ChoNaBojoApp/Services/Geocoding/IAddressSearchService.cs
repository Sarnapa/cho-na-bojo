namespace ChoNaBojo.App.Services.Geocoding;

public interface IAddressSearchService
{
	Task<AddressSearchResult> SearchAsync(
		string query,
		CancellationToken cancellationToken);
}
