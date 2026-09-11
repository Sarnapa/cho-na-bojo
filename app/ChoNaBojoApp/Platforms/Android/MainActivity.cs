using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using ChoNaBojo.App.Services.Push;
using ChoNaBojo.Contracts.Consts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace ChoNaBojo.App
{
	[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
	public class MainActivity: MauiAppCompatActivity
	{
		#region Overrides
		protected override void OnCreate(Bundle? savedInstanceState)
		{
			base.OnCreate(savedInstanceState);
			TryEnqueuePushIntent(Intent);
		}

		protected override void OnNewIntent(Intent? intent)
		{
			base.OnNewIntent(intent);
			if (intent is null)
			{
				return;
			}

			Intent = intent;
			IPushNavigationRouter? router = TryEnqueuePushIntent(intent);
			if (router is not null)
			{
				_ = router.ConsumePendingAsync();
			}
		}
		#endregion

		#region Private methods
		private static IPushNavigationRouter? TryEnqueuePushIntent(Intent? intent)
		{
			if (intent is null)
			{
				return null;
			}

			string[] dataKeys =
			[
				PushPolicy.DataKeys.SchemaVersion,
				PushPolicy.DataKeys.Type,
				PushPolicy.DataKeys.EventId,
				PushPolicy.DataKeys.JoinRequestId,
				PushPolicy.DataKeys.NotificationId,
				PushPolicy.DataKeys.SentAtUtc
			];
			var data = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (string key in dataKeys)
			{
				string? value = intent.GetStringExtra(key);
				if (value is not null)
				{
					data[key] = value;
				}
			}

			if (!PushNotificationPayload.TryParse(data, out PushNotificationPayload? payload))
			{
				return null;
			}

			IPushNavigationRouter? router = IPlatformApplication.Current?.Services
				.GetService<IPushNavigationRouter>();
			if (router?.TryEnqueue(payload!) != true)
			{
				return null;
			}

			foreach (string key in dataKeys)
			{
				intent.RemoveExtra(key);
			}

			return router;
		}
		#endregion
	}
}
