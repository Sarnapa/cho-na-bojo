using ChoNaBojo.Contracts.DTOs;

namespace ChoNaBojo.App.Services.Venues;

#region VenueCatalogLoadStatus
public enum VenueCatalogLoadStatus
{
	Success,
	Unauthorized,
	Network,
	Unknown
}
#endregion

#region VenueCatalogLoadResult
/// <summary>Outcome of <see cref="IVenueCatalog.EnsureLoadedAsync"/>.</summary>
public sealed record VenueCatalogLoadResult(VenueCatalogLoadStatus Status)
{
	public static readonly VenueCatalogLoadResult Success = new(VenueCatalogLoadStatus.Success);
	public static readonly VenueCatalogLoadResult Unauthorized = new(VenueCatalogLoadStatus.Unauthorized);
	public static readonly VenueCatalogLoadResult Network = new(VenueCatalogLoadStatus.Network);
	public static readonly VenueCatalogLoadResult Unknown = new(VenueCatalogLoadStatus.Unknown);
}
#endregion

/// <summary>
/// Session-scoped, in-memory cache of the venue/sport catalog. Loaded once per app session —
/// filtering and re-entry are then pure in-memory work with no further network calls.
///
/// Scale caveat: this holds the *entire* venue set. That is correct only while the dataset is
/// one city / ~100 rows. The intended evolution is a viewport-bounded (bbox) query against the
/// GiST index on <c>Venue.Location</c>; switching to it requires a map control with a reliable
/// camera-idle event (the official control's Android <c>VisibleRegion</c> behaviour is broken,
/// see https://github.com/dotnet/maui/issues/21094).
/// </summary>
public interface IVenueCatalog
{
	IReadOnlyList<VenueResponse> Venues { get; }

	IReadOnlyList<SportResponse> Sports { get; }

	/// <summary>
	/// Loads venues and sports concurrently on first call; subsequent concurrent callers await
	/// the same in-flight load rather than triggering duplicate fetches. A failed load leaves
	/// the cache empty so a later retry re-fetches from scratch.
	/// </summary>
	Task<VenueCatalogLoadResult> EnsureLoadedAsync(CancellationToken cancellationToken);
}
