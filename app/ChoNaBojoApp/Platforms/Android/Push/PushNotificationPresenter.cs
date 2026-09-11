using Android.App;
using Android.Content;
using ChoNaBojo.App.Services.Push;
using ChoNaBojo.Contracts.Consts;
using ChoNaBojo.Contracts.Enums;
using AndroidX.Core.Content;

namespace ChoNaBojo.App.Platforms.Android.Push;

public sealed class PushNotificationPresenter
{
	#region Private constants
	private const string NotificationTitle = "ChoNaBojo";
	#endregion

	#region Public methods
	public void Show(Context context, PushNotificationPayload payload)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(payload);
		if (!payload.IsValid)
		{
			throw new ArgumentException(
				"The notification payload is invalid.",
				nameof(payload));
		}

		var tapIntent = new Intent(context, typeof(MainActivity));
		tapIntent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
		tapIntent.PutExtra(PushPolicy.DataKeys.SchemaVersion, PushPolicy.SchemaVersion);
		tapIntent.PutExtra(PushPolicy.DataKeys.Type, ((int)payload.Type).ToString());
		tapIntent.PutExtra(PushPolicy.DataKeys.EventId, payload.EventId.ToString("D"));
		tapIntent.PutExtra(
			PushPolicy.DataKeys.JoinRequestId,
			payload.JoinRequestId.ToString("D"));
		tapIntent.PutExtra(
			PushPolicy.DataKeys.NotificationId,
			payload.NotificationId.ToString("D"));
		tapIntent.PutExtra(
			PushPolicy.DataKeys.SentAtUtc,
			payload.SentAtUtc.ToString("O"));

		int requestCode = payload.NotificationId.GetHashCode() & int.MaxValue;
		PendingIntent contentIntent = PendingIntent.GetActivity(
			context,
			requestCode,
			tapIntent,
			PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;

		using var builder = new Notification.Builder(
			context,
			NotificationChannels.EventUpdatesId);
		builder
			.SetSmallIcon(Resource.Drawable.ic_stat_notification)
			.SetColor(ContextCompat.GetColor(context, Resource.Color.notification_color))
			.SetContentTitle(NotificationTitle)
			.SetContentText(BodyFor(payload.Type))
			.SetContentIntent(contentIntent)
			.SetAutoCancel(true)
			.SetCategory(Notification.CategoryEvent);

		using Notification notification = builder.Build();
		var notificationManager =
			(NotificationManager?)context.GetSystemService(Context.NotificationService);
		notificationManager?.Notify(
			payload.NotificationId.ToString("D"),
			0,
			notification);
	}
	#endregion

	#region Private methods
	private static string BodyFor(PushNotificationType type)
	{
		return type switch
		{
			PushNotificationType.JoinRequestCreated =>
				"Someone asked to join your event.",
			PushNotificationType.JoinRequestAccepted =>
				"Your join request was accepted.",
			PushNotificationType.JoinRequestRejected =>
				"Your join request was declined.",
			_ => throw new ArgumentOutOfRangeException(
				nameof(type),
				type,
				"The notification type is undefined.")
		};
	}
	#endregion
}
