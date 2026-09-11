using ChoNaBojo.Contracts.Enums;

namespace ChoNaBojo.Server.Data.Entities;

/// <summary>
/// A durable notification intent created atomically with its domain transition.
/// </summary>
public class PushOutboxItem
{
	/// <summary>Primary key; database-generated UUID.</summary>
	public Guid Id { get; set; }

	public string EventKey { get; set; } = null!;

	public Guid RecipientUserId { get; set; }

	public PushNotificationType Type { get; set; }

	public Guid SportsEventId { get; set; }

	public Guid EventJoinRequestId { get; set; }

	public Guid NotificationId { get; set; }

	public DateTime OccurredUtc { get; set; }

	public DateTime NextAttemptUtc { get; set; }

	public DateTime? ClaimedUtc { get; set; }

	public DateTime? CompletedUtc { get; set; }

	public User RecipientUser { get; set; } = null!;

	public SportsEvent SportsEvent { get; set; } = null!;

	public EventJoinRequest EventJoinRequest { get; set; } = null!;

	public ICollection<PushDelivery> Deliveries { get; set; } = [];
}
