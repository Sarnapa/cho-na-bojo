namespace ChoNaBojo.App.Services.Push;

#if ANDROID
public sealed class PushPermissionService : IPushPermissionService
{
	#region Private constants
	private const string PermissionRequestedKey = "push_notification_permission_requested";
	#endregion

	#region Public methods
	public async Task RequestIfNeededAsync(CancellationToken cancellationToken)
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(33)
			|| Preferences.Default.Get(PermissionRequestedKey, false))
		{
			return;
		}

		PermissionStatus status =
			await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
		cancellationToken.ThrowIfCancellationRequested();
		if (status == PermissionStatus.Granted)
		{
			Preferences.Default.Set(PermissionRequestedKey, true);
			return;
		}

		// Record before presenting either dialog so lifecycle interruption cannot cause a
		// second automatic prompt on the next appearance.
		Preferences.Default.Set(PermissionRequestedKey, true);
		await MainThread.InvokeOnMainThreadAsync(async () =>
		{
			Page? page = Application.Current?.Windows.Count > 0
				? Application.Current.Windows[0].Page
				: null;
			if (page is null)
			{
				return;
			}

			await page.DisplayAlertAsync(
				"Stay up to date",
				"Allow notifications to hear when someone joins your event or responds to your request.",
				"Continue");
			cancellationToken.ThrowIfCancellationRequested();
			await Permissions.RequestAsync<Permissions.PostNotifications>();
		});
	}
	#endregion
}
#endif

public sealed class NoOpPushPermissionService : IPushPermissionService
{
	public Task RequestIfNeededAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
