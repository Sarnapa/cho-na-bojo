using Android.App;
using Android.Content;
using Android.OS;

namespace ChoNaBojo.App.Platforms.Android.Push;

public static class NotificationChannels
{
	public const string EventUpdatesId = "event_updates";

	public static void EnsureCreated(Context context)
	{
		if (Build.VERSION.SdkInt < BuildVersionCodes.O)
		{
			return;
		}

		var channel = new NotificationChannel(
			EventUpdatesId,
			"Event updates",
			NotificationImportance.High);
		var notificationManager =
			(NotificationManager?)context.GetSystemService(Context.NotificationService);
		notificationManager?.CreateNotificationChannel(channel);
	}
}
