namespace ChoNaBojo.Server.Data.Entities;

/// <summary>
/// Explicit many-to-many join between a <see cref="Venue"/> and a <see cref="Sport"/>.
/// Composite key <c>(VenueId, SportId)</c>.
/// </summary>
public class VenueSport
{
	public int VenueId
	{
		get; set;
	}
	public int SportId
	{
		get; set;
	}

	public Venue Venue { get; set; } = null!;
	public Sport Sport { get; set; } = null!;
	public ICollection<SportsEvent> SportsEvents { get; set; } = [];
}
