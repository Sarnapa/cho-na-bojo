namespace ChoNaBojo.Contracts.Consts;

public static class PushPolicy
{
	public const int DeviceRegistrationIdMaxLength = 512;
	public const int AppVersionMaxLength = 64;
	public const string SchemaVersion = "1";

	public static class DataKeys
	{
		public const string SchemaVersion = "schemaVersion";
		public const string Type = "type";
		public const string EventId = "eventId";
		public const string JoinRequestId = "joinRequestId";
		public const string NotificationId = "notificationId";
		public const string SentAtUtc = "sentAtUtc";
	}
}
