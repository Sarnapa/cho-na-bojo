using NetTopologySuite.Geometries;

namespace ChoNaBojo.Server.Data.Entities;

/// <summary>
/// A sports venue. Seeded at migrate-time from <c>data/warsaw-venues.csv</c> using the
/// stable CSV <see cref="Id"/>.
/// </summary>
public class Venue
{
	/// <summary>Stable CSV-sourced identifier (explicit, never database-generated).</summary>
	public int Id
	{
		get; set;
	}

	/// <summary>Venue name.</summary>
	public string Name { get; set; } = null!;

	/// <summary>Spatial location as a PostGIS <c>geometry(Point,4326)</c>: X = longitude, Y = latitude.</summary>
	public Point Location { get; set; } = null!;

	/// <summary>Address.</summary>
	public string Address { get; set; } = null!;

	/// <summary>Description.</summary>
	public string Description { get; set; } = null!;

	public ICollection<VenueSport> VenueSports { get; set; } = [];
}
