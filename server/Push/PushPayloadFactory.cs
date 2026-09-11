using System.Globalization;
using ChoNaBojo.Contracts.Consts;
using ChoNaBojo.Contracts.Enums;
using ChoNaBojo.Server.Data.Entities;

namespace ChoNaBojo.Server.Push;

public static class PushPayloadFactory
{
	#region Private constants
	private const string NotificationTitle = "ChoNaBojo";
	#endregion

	#region Public methods
	public static PushMessage Create(
		PushOutboxItem outboxItem,
		string deviceRegistrationId,
		DateTime sentAtUtc)
	{
		ArgumentNullException.ThrowIfNull(outboxItem);
		ArgumentException.ThrowIfNullOrWhiteSpace(deviceRegistrationId);

		if (!Enum.IsDefined(outboxItem.Type))
		{
			throw new ArgumentOutOfRangeException(
				nameof(outboxItem),
				outboxItem.Type,
				"The outbox item has an undefined notification type.");
		}

		if (outboxItem.SportsEventId == Guid.Empty
			|| outboxItem.EventJoinRequestId == Guid.Empty
			|| outboxItem.NotificationId == Guid.Empty)
		{
			throw new ArgumentException(
				"The outbox item contains an empty payload identifier.",
				nameof(outboxItem));
		}

		string body = outboxItem.Type switch
		{
			PushNotificationType.JoinRequestCreated =>
				"Someone asked to join your event.",
			PushNotificationType.JoinRequestAccepted =>
				"Your join request was accepted.",
			PushNotificationType.JoinRequestRejected =>
				"Your join request was declined.",
			_ => throw new ArgumentOutOfRangeException(
				nameof(outboxItem),
				outboxItem.Type,
				"The outbox item has an undefined notification type.")
		};

		var data = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[PushPolicy.DataKeys.SchemaVersion] = PushPolicy.SchemaVersion,
			[PushPolicy.DataKeys.Type] =
				((int)outboxItem.Type).ToString(CultureInfo.InvariantCulture),
			[PushPolicy.DataKeys.EventId] =
				outboxItem.SportsEventId.ToString("D"),
			[PushPolicy.DataKeys.JoinRequestId] =
				outboxItem.EventJoinRequestId.ToString("D"),
			[PushPolicy.DataKeys.NotificationId] =
				outboxItem.NotificationId.ToString("D"),
			[PushPolicy.DataKeys.SentAtUtc] =
				sentAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
		};

		return new PushMessage(
			deviceRegistrationId,
			NotificationTitle,
			body,
			data,
			outboxItem.NotificationId);
	}
	#endregion
}
