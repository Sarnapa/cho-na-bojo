namespace ChoNaBojo.App.Services.Feedback;

/// <summary>
/// Centralizes transient user feedback (ui-guidelines §9: Snackbar on <c>OnSurfaceColor</c>
/// background, auto-dismiss ~3s) so every ViewModel shows messages identically and a failure to
/// render the Snackbar can never silently vanish inside a fire-and-forget <c>[RelayCommand]</c>.
/// </summary>
public interface IFeedbackService
{
	Task ShowSnackbarAsync(string message, CancellationToken cancellationToken = default);
}
