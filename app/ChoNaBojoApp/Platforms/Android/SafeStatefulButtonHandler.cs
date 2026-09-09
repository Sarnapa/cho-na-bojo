using Google.Android.Material.Button;
using UraniumUI.Handlers;

namespace ChoNaBojo.App.Platforms.Android;

// UraniumUI's StatefulButtonHandler.DisconnectHandler calls ResetState(), which runs
// VisualStateManager.GoToState on a button whose handler MAUI has already detached from
// its platform view. Unapplying the visual-state setters clears bindable properties,
// re-enters the button property mapper (MapTextColor) and throws
// InvalidOperationException: "PlatformView cannot be null here".
//
// The throw happens inside Page.SendNavigatedFrom during PopModalAsync, so it aborts
// Shell's navigation pipeline mid-pop and leaves the modal stack inconsistent - after
// that no other venue modal can be opened. Swallowing it here keeps teardown and the
// navigation pipeline intact; the button and its page are being destroyed anyway.
public class SafeStatefulButtonHandler : StatefulButtonHandler
{
	#region Overrides
	protected override void DisconnectHandler(MaterialButton platformView)
	{
		try
		{
			base.DisconnectHandler(platformView);
		}
		catch (InvalidOperationException ex)
		{
			System.Diagnostics.Debug.WriteLine(
				$"SafeStatefulButtonHandler: ignored teardown failure: {ex.Message}");
		}
	}
	#endregion
}
