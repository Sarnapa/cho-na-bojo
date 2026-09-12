using System.Globalization;
using ChoNaBojo.Contracts.Consts;
using ChoNaBojo.Contracts.Enums;

namespace ChoNaBojo.App.Services.Push;

public sealed record PushNotificationPayload(
	PushNotificationType Type,
	Guid EventId,
	Guid JoinRequestId,
	Guid NotificationId,
	DateTimeOffset SentAtUtc)
{
	public bool IsValid =>
		Type is PushNotificationType.JoinRequestCreated
			or PushNotificationType.JoinRequestAccepted
			or PushNotificationType.JoinRequestRejected
			or PushNotificationType.EventCancelled
			or PushNotificationType.ParticipantRemoved
			or PushNotificationType.ParticipantLeft
		&& EventId != Guid.Empty
		&& JoinRequestId != Guid.Empty
		&& NotificationId != Guid.Empty;

	public static bool TryParse(
		IEnumerable<KeyValuePair<string, string>> values,
		out PushNotificationPayload? payload)
	{
		int rawType = 0;
		Guid eventId = Guid.Empty;
		Guid joinRequestId = Guid.Empty;
		Guid notificationId = Guid.Empty;
		DateTimeOffset sentAtUtc = default;
		IReadOnlyDictionary<string, string> data = values.ToDictionary(
			item => item.Key,
			item => item.Value,
			StringComparer.Ordinal);

		bool isValid =
			data.TryGetValue(PushPolicy.DataKeys.SchemaVersion, out string? schemaVersion)
			&& string.Equals(schemaVersion, PushPolicy.SchemaVersion, StringComparison.Ordinal)
			&& data.TryGetValue(PushPolicy.DataKeys.Type, out string? typeValue)
			&& int.TryParse(
				typeValue,
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out rawType)
			&& Enum.IsDefined(typeof(PushNotificationType), rawType)
			&& data.TryGetValue(PushPolicy.DataKeys.EventId, out string? eventIdValue)
			&& Guid.TryParseExact(eventIdValue, "D", out eventId)
			&& eventId != Guid.Empty
			&& data.TryGetValue(PushPolicy.DataKeys.JoinRequestId, out string? joinRequestIdValue)
			&& Guid.TryParseExact(joinRequestIdValue, "D", out joinRequestId)
			&& joinRequestId != Guid.Empty
			&& data.TryGetValue(PushPolicy.DataKeys.NotificationId, out string? notificationIdValue)
			&& Guid.TryParseExact(notificationIdValue, "D", out notificationId)
			&& notificationId != Guid.Empty
			&& data.TryGetValue(PushPolicy.DataKeys.SentAtUtc, out string? sentAtUtcValue)
			&& DateTimeOffset.TryParseExact(
				sentAtUtcValue,
				"O",
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal,
				out sentAtUtc);

		payload = isValid
			? new(
				(PushNotificationType)rawType,
				eventId,
				joinRequestId,
				notificationId,
				sentAtUtc.ToUniversalTime())
			: null;
		return payload is not null;
	}
}

public interface IPushNavigationRouter
{
	bool TryEnqueue(PushNotificationPayload payload);

	Task ConsumePendingAsync();

	Task RefreshVisibleMyEventsAsync();

	void Clear();
}
