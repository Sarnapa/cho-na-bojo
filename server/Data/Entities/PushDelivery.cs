namespace ChoNaBojo.Server.Data.Entities;

/// <summary>
/// Delivery state for one outbox notification and one app installation.
/// </summary>
public class PushDelivery
{
	/// <summary>Primary key; database-generated UUID.</summary>
	public Guid Id { get; set; }

	public Guid PushOutboxItemId { get; set; }

	public Guid PushInstallationId { get; set; }

	public int AttemptCount { get; set; }

	public DateTime NextAttemptUtc { get; set; }

	public DateTime? AcceptedUtc { get; set; }

	public DateTime? DeadLetteredUtc { get; set; }

	public string? LastErrorCode { get; set; }

	public string? FcmMessageId { get; set; }

	public PushOutboxItem PushOutboxItem { get; set; } = null!;

	public PushInstallation PushInstallation { get; set; } = null!;
}
