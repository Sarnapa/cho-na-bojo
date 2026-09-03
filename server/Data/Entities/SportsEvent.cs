namespace ChoNaBojo.Server.Data.Entities;

/// <summary>
/// A sports event organized by one user at one supported venue and sport pairing.
/// </summary>
public class SportsEvent
{
	/// <summary>Primary key; database-generated UUID.</summary>
	public Guid Id { get; set; }

	/// <summary>The user who created and manages this event.</summary>
	public Guid OrganizerUserId { get; set; }

	/// <summary>Client-generated id used to make creation retries idempotent per organizer.</summary>
	public Guid ClientRequestId { get; set; }

	public int VenueId { get; set; }

	public int SportId { get; set; }

	public string Title { get; set; } = null!;

	public string? Description { get; set; }

	public DateTime StartsAtUtc { get; set; }

	public DateTime EstimatedEndsAtUtc { get; set; }

	public DateTime CreatedUtc { get; set; }

	public int ParticipantLimit { get; set; }

	public bool AutoAccept { get; set; }

	public User Organizer { get; set; } = null!;

	public VenueSport VenueSport { get; set; } = null!;
}
