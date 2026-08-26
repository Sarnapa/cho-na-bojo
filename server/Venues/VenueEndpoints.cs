using Microsoft.EntityFrameworkCore;
using ChoNaBojo.Server.Data;
using ChoNaBojo.Contracts.DTOs;

namespace ChoNaBojo.Server.Venues;

/// <summary>
/// Read-only venue and sport catalog endpoints. Authorization is inherited from the
/// <c>/api</c> group these are mapped onto — no per-endpoint <c>RequireAuthorization()</c>.
///
/// Scale caveat: this loads the full venue set in one request. That is correct only while
/// the dataset is one city / ~100 rows. The intended evolution is a viewport-bounded (bbox)
/// query; the GiST index on <c>Venue.Location</c> (see <see cref="ChoNaBojoContext"/>) already
/// exists to serve it. Switching to it will require a map control with a reliable camera-idle
/// event — the official control's Android <c>VisibleRegion</c> behaviour is broken
/// (see https://github.com/dotnet/maui/issues/21094).
/// </summary>
public static class VenueEndpoints
{
	public static IEndpointRouteBuilder MapVenueEndpoints(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet("/venues", GetVenuesAsync)
			.WithName("VenuesList");

		endpoints.MapGet("/sports", GetSportsAsync)
			.WithName("SportsList");

		return endpoints;
	}

	private static async Task<IResult> GetVenuesAsync(
		ChoNaBojoContext dbContext,
		CancellationToken cancellationToken)
	{
		var venues = await dbContext.Venues
			.AsNoTracking()
			.OrderBy(v => v.Name)
			.Select(v => new VenueResponse(
				v.Id,
				v.Name,
				v.Address,
				v.Description,
				v.Location.Y,
				v.Location.X,
				v.VenueSports.Select(vs => vs.SportId).ToList()))
			.ToListAsync(cancellationToken);

		return Results.Ok(venues);
	}

	private static async Task<IResult> GetSportsAsync(
		ChoNaBojoContext dbContext,
		CancellationToken cancellationToken)
	{
		var sports = await dbContext.Sports
			.AsNoTracking()
			.OrderBy(s => s.Id)
			.Select(s => new SportResponse(s.Id, s.Code, s.Name))
			.ToListAsync(cancellationToken);

		return Results.Ok(sports);
	}
}
