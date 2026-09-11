using Android.App;
using Android.Content;
using Android.Util;
using ChoNaBojo.App.Services.Push;
using Firebase.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace ChoNaBojo.App.Platforms.Android.Push;

[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public sealed class ChoNaBojoMessagingService: FirebaseMessagingService
{
	#region Private Constants
	private const string LogTag = "ChoNaBojo.Push";
	#endregion

	#region Overrides
	public override void OnRegistered(string installationId)
	{
		IServiceProvider? services = IPlatformApplication.Current?.Services;
		IPushRegistrationStore registrationStore =
			services?.GetService<IPushRegistrationStore>() ?? new PushRegistrationStore();
		registrationStore.SetLatestRegistrationId(installationId);

		IPushRegistrationService? registrationService =
			services?.GetService<IPushRegistrationService>();
		if (registrationService is not null)
		{
			_ = ObserveAsync(
				() => registrationService.OnRegistrationIdChangedAsync(
					installationId,
					CancellationToken.None),
				"Push registration identifier update");
		}

		Log.Info(LogTag, "Firebase registration identifier received (length {0}).", installationId.Length);
	}

	public override void OnUnregistered(string installationId)
	{
		Log.Info(LogTag, "Firebase registration identifier removed (length {0}).", installationId.Length);
	}

	public override void OnMessageReceived(RemoteMessage message)
	{
		if (!PushNotificationPayload.TryParse(
				message.Data,
				out PushNotificationPayload? payload))
		{
			return;
		}

		IServiceProvider? services = IPlatformApplication.Current?.Services;
		PushNotificationPresenter? presenter =
			services?.GetService<PushNotificationPresenter>();
		IPushNavigationRouter? router =
			services?.GetService<IPushNavigationRouter>();
		if (presenter is null || router is null)
		{
			return;
		}

		presenter.Show(this, payload!);
		MainThread.BeginInvokeOnMainThread(
			() => _ = ObserveAsync(
				router.RefreshVisibleMyEventsAsync,
				"Refresh of the visible My Events page after a received message"));
	}

	public override void OnDeletedMessages()
	{
		IPushNavigationRouter? router = IPlatformApplication.Current?.Services
			.GetService<IPushNavigationRouter>();
		if (router is not null)
		{
			MainThread.BeginInvokeOnMainThread(
				() => _ = ObserveAsync(
					router.RefreshVisibleMyEventsAsync,
					"Refresh of the visible My Events page after dropped messages"));
		}
	}
	#endregion

	#region Private methods
	/// <summary>
	/// Awaits a fire-and-forget operation so its failure is logged instead of
	/// surfacing as an unobserved task exception on a background thread.
	/// </summary>
	private static async Task ObserveAsync(
		Func<Task> operation,
		string description)
	{
		try
		{
			await operation();
		}
		catch (Exception exception)
		{
			Log.Warn(LogTag, $"{description} failed: {exception}");
		}
	}
	#endregion
}
