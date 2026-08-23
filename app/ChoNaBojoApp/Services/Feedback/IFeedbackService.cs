namespace ChoNaBojo.App.Services.Feedback;

/// <summary>
/// Centralizes transient user feedback (ui-guidelines §9: Snackbar on <c>OnSurfaceColor</c>
/// background, auto-dismiss ~3s) so every ViewModel shows messages identically and a failure to
/// render the Snackbar can never silently vanish inside a fire-and-forget <c>[RelayCommand]</c>.
/// </summary>
public interface IFeedbackService
{
	Task ShowSnackbarAsync(string message, CancellationToken cancellationToken = default);

	/// <summary>
	/// Shows an MD3 destructive confirm (ui-guidelines §9: SurfaceColor dialog, ErrorColor
	/// confirm button) and awaits the user's choice. Returns <c>true</c> only when the confirm
	/// button was tapped — dismissing by tapping outside the dialog is treated as cancel.
	/// </summary>
	Task<bool> ShowConfirmAsync(string title, string message, string confirmText, string cancelText, CancellationToken cancellationToken = default);
}
