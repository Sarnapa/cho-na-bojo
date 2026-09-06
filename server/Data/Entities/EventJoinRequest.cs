using ChoNaBojo.Contracts.Enums;

namespace ChoNaBojo.Server.Data.Entities;

/// <summary>
/// A user's request to join a sports event.
/// </summary>
public class EventJoinRequest
{
	/// <summary>Primary key; database-generated UUID.</summary>
	public Guid Id { get; set; }

	public Guid SportsEventId { get; set; }

	public Guid RequesterUserId { get; set; }

	public EventJoinRequestStatus Status { get; set; }

	/// <summary>UTC timestamp when the request was created.</summary>
	public DateTime CreatedUtc { get; set; }

	/// <summary>UTC timestamp of the most recent status update.</summary>
	public DateTime? UpdatedUtc { get; set; }

	public SportsEvent SportsEvent { get; set; } = null!;

	public User Requester { get; set; } = null!;
}
