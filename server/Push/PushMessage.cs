namespace ChoNaBojo.Server.Push;

public sealed record PushMessage(
	string DeviceRegistrationId,
	string Title,
	string Body,
	IReadOnlyDictionary<string, string> Data,
	Guid NotificationId);
