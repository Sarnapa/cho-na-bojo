using CommunityToolkit.Maui.Views;

namespace ChoNaBojo.App.Views.Popups;

/// <inheritdoc cref="ConfirmDialog" path="/summary" />
public partial class ConfirmDialog : Popup<bool>
{
	#region Constructors
	public ConfirmDialog(string title, string message, string confirmText, string cancelText)
	{
		InitializeComponent();
		TitleLabel.Text = title;
		MessageLabel.Text = message;
		ConfirmButton.Text = confirmText;
		CancelButton.Text = cancelText;
	}
	#endregion

	#region Events handlers
	private async void OnConfirmClicked(object? sender, EventArgs e)
	{
		await CloseAsync(true);
	}

	private async void OnCancelClicked(object? sender, EventArgs e)
	{
		await CloseAsync(false);
	}
	#endregion
}
