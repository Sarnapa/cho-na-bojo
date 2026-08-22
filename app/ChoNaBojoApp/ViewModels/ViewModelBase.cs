using CommunityToolkit.Mvvm.ComponentModel;

namespace ChoNaBojo.App.ViewModels;

/// <summary>
/// Common ViewModel plumbing: a single busy flag every screen's commands/spinners bind to,
/// plus its negation so XAML can gate <c>IsEnabled</c> without needing a value converter.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
	#region Observable properties
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsNotBusy))]
	private bool isBusy;
	#endregion

	#region Public properties
	public bool IsNotBusy
	{
		get
		{
			return !IsBusy;
		}
	}
	#endregion
}
