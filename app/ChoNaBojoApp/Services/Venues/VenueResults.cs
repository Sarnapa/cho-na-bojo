using ChoNaBojo.Contracts.DTOs;

namespace ChoNaBojo.App.Services.Venues;

#region VenueCatalogResultStatus
public enum VenueCatalogResultStatus
{
	Success,
	Unauthorized,
	Network,
	Unknown
}
#endregion

#region VenueCatalogResult
/// <summary>Client-side result of the protected <c>GET /api/venues</c> call.</summary>
public sealed record VenueCatalogResult
{
	#region Properties
	public VenueCatalogResultStatus Status { get; }
	public IReadOnlyList<VenueResponse>? Venues { get; }
	#endregion

	#region Constructors
	private VenueCatalogResult(VenueCatalogResultStatus status, IReadOnlyList<VenueResponse>? venues)
	{
		Status = status;
		Venues = venues;
	}
	#endregion

	#region Public methods
	public static VenueCatalogResult Success(IReadOnlyList<VenueResponse> venues)
	{
		return new(VenueCatalogResultStatus.Success, venues);
	}

	public static VenueCatalogResult Unauthorized()
	{
		return new(VenueCatalogResultStatus.Unauthorized, null);
	}

	public static VenueCatalogResult Network()
	{
		return new(VenueCatalogResultStatus.Network, null);
	}

	public static VenueCatalogResult Unknown()
	{
		return new(VenueCatalogResultStatus.Unknown, null);
	}
	#endregion
}
#endregion

#region SportCatalogResultStatus
public enum SportCatalogResultStatus
{
	Success,
	Unauthorized,
	Network,
	Unknown
}
#endregion

#region SportCatalogResult
/// <summary>Client-side result of the protected <c>GET /api/sports</c> call.</summary>
public sealed record SportCatalogResult
{
	#region Properties
	public SportCatalogResultStatus Status { get; }
	public IReadOnlyList<SportResponse>? Sports { get; }
	#endregion

	#region Constructors
	private SportCatalogResult(SportCatalogResultStatus status, IReadOnlyList<SportResponse>? sports)
	{
		Status = status;
		Sports = sports;
	}
	#endregion

	#region Public methods
	public static SportCatalogResult Success(IReadOnlyList<SportResponse> sports)
	{
		return new(SportCatalogResultStatus.Success, sports);
	}

	public static SportCatalogResult Unauthorized()
	{
		return new(SportCatalogResultStatus.Unauthorized, null);
	}

	public static SportCatalogResult Network()
	{
		return new(SportCatalogResultStatus.Network, null);
	}

	public static SportCatalogResult Unknown()
	{
		return new(SportCatalogResultStatus.Unknown, null);
	}
	#endregion
}
#endregion
