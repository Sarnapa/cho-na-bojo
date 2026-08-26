using ChoNaBojo.Contracts.DTOs;

namespace ChoNaBojo.App.Services.Venues;

/// <inheritdoc cref="IVenueCatalog" />
public sealed class VenueCatalog : IVenueCatalog
{
	#region Private fields
	private readonly IApiService _apiService;

	// Guards the load so concurrent callers (e.g. the map's AppearingAsync racing a retry)
	// await one in-flight fetch instead of issuing duplicate requests.
	private readonly SemaphoreSlim _loadLock = new(1, 1);

	private List<VenueResponse> _venues = [];
	private List<SportResponse> _sports = [];
	private volatile bool _isLoaded;
	#endregion

	#region Constructors
	public VenueCatalog(IApiService apiService)
	{
		_apiService = apiService;
	}
	#endregion

	#region Properties
	public IReadOnlyList<VenueResponse> Venues
	{
		get
		{
			return _venues;
		}
	}

	public IReadOnlyList<SportResponse> Sports
	{
		get
		{
			return _sports;
		}
	}
	#endregion

	#region Public methods
	public async Task<VenueCatalogLoadResult> EnsureLoadedAsync(CancellationToken cancellationToken)
	{
		if (_isLoaded)
		{
			return VenueCatalogLoadResult.Success;
		}

		await _loadLock.WaitAsync(cancellationToken);
		try
		{
			// Another caller may have completed the load while we waited for the lock.
			if (_isLoaded)
			{
				return VenueCatalogLoadResult.Success;
			}

			Task<VenueCatalogResult> venuesTask = _apiService.GetVenuesAsync(cancellationToken);
			Task<SportCatalogResult> sportsTask = _apiService.GetSportsAsync(cancellationToken);
			await Task.WhenAll(venuesTask, sportsTask);

			VenueCatalogResult venuesResult = await venuesTask;
			SportCatalogResult sportsResult = await sportsTask;

			if (venuesResult.Status == VenueCatalogResultStatus.Unauthorized
				|| sportsResult.Status == SportCatalogResultStatus.Unauthorized)
			{
				return VenueCatalogLoadResult.Unauthorized;
			}

			if (venuesResult.Status == VenueCatalogResultStatus.Network
				|| sportsResult.Status == SportCatalogResultStatus.Network)
			{
				return VenueCatalogLoadResult.Network;
			}

			if (venuesResult.Status != VenueCatalogResultStatus.Success
				|| sportsResult.Status != SportCatalogResultStatus.Success
				|| venuesResult.Venues is null
				|| sportsResult.Sports is null)
			{
				return VenueCatalogLoadResult.Unknown;
			}

			_venues = [.. venuesResult.Venues];
			_sports = [.. sportsResult.Sports];
			_isLoaded = true;

			return VenueCatalogLoadResult.Success;
		}
		finally
		{
			_loadLock.Release();
		}
	}
	#endregion
}
