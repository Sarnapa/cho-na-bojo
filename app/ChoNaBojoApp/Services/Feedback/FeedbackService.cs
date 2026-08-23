using System.Diagnostics;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using ChoNaBojo.App.Views.Popups;

namespace ChoNaBojo.App.Services.Feedback;

/// <inheritdoc cref="IFeedbackService" />
public class FeedbackService : IFeedbackService
{
	#region Private fields
	private static readonly TimeSpan SnackbarDuration = TimeSpan.FromSeconds(3);
	#endregion

	#region Public methods
	public async Task ShowSnackbarAsync(string message, CancellationToken cancellationToken = default)
	{
		try
		{
			ISnackbar snackbar = Snackbar.Make(message, duration: SnackbarDuration, visualOptions: BuildOptions());
			await snackbar.Show(cancellationToken);
		}
		catch (Exception ex)
		{
			// A Snackbar failure must never disappear silently inside a fire-and-forget
			// [RelayCommand] task — log it and fall back to a guaranteed-visible native alert
			// so the user always learns what happened.
			Debug.WriteLine($"[{nameof(FeedbackService)}] Snackbar failed, falling back to alert: {ex}");
			await ShowFallbackAlertAsync(message);
		}
	}

	public async Task<bool> ShowConfirmAsync(string title, string message, string confirmText, string cancelText, CancellationToken cancellationToken = default)
	{
		Page? page = Application.Current?.Windows.Count > 0 ? Application.Current.Windows[0].Page : null;
		if (page is null)
		{
			// No page to host the popup on — fail closed (never treat "can't ask" as "confirmed").
			return false;
		}

		var popup = new ConfirmDialog(title, message, confirmText, cancelText);
		IPopupResult<bool> result = await page.ShowPopupAsync<bool>(popup, PopupOptions.Empty, cancellationToken);
		return !result.WasDismissedByTappingOutsideOfPopup && result.Result;
	}
	#endregion

	#region Private methods
	private static SnackbarOptions BuildOptions()
	{
		return new SnackbarOptions
		{
			BackgroundColor = (Color)Application.Current!.Resources["OnSurfaceColor"],
			TextColor = (Color)Application.Current!.Resources["SurfaceColor"],
			CornerRadius = new CornerRadius(8)
		};
	}

	private static Task ShowFallbackAlertAsync(string message)
	{
		Page? page = Application.Current?.Windows.Count > 0 ? Application.Current.Windows[0].Page : null;
		return page is not null ? page.DisplayAlertAsync("Notice", message, "OK") : Task.CompletedTask;
	}
	#endregion
}
