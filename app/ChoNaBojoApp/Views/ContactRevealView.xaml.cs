using System.Collections;
using System.Windows.Input;

namespace ChoNaBojo.App.Views;

public partial class ContactRevealView : ContentView
{
	public static readonly BindableProperty IsLockedProperty = BindableProperty.Create(
		nameof(IsLocked),
		typeof(bool),
		typeof(ContactRevealView),
		true);

	public static readonly BindableProperty IsRevealedProperty = BindableProperty.Create(
		nameof(IsRevealed),
		typeof(bool),
		typeof(ContactRevealView),
		false);

	public static readonly BindableProperty ShowLoadActionProperty = BindableProperty.Create(
		nameof(ShowLoadAction),
		typeof(bool),
		typeof(ContactRevealView),
		false);

	public static readonly BindableProperty CanLoadProperty = BindableProperty.Create(
		nameof(CanLoad),
		typeof(bool),
		typeof(ContactRevealView),
		false);

	public static readonly BindableProperty LoadActionLabelProperty = BindableProperty.Create(
		nameof(LoadActionLabel),
		typeof(string),
		typeof(ContactRevealView),
		string.Empty);

	public static readonly BindableProperty ContactRowsProperty = BindableProperty.Create(
		nameof(ContactRows),
		typeof(IEnumerable),
		typeof(ContactRevealView));

	public static readonly BindableProperty LoadCommandProperty = BindableProperty.Create(
		nameof(LoadCommand),
		typeof(ICommand),
		typeof(ContactRevealView));

	public static readonly BindableProperty LoadCommandParameterProperty =
		BindableProperty.Create(
			nameof(LoadCommandParameter),
			typeof(object),
			typeof(ContactRevealView));

	public static readonly BindableProperty OpenCommandProperty = BindableProperty.Create(
		nameof(OpenCommand),
		typeof(ICommand),
		typeof(ContactRevealView));

	public static readonly BindableProperty CopyCommandProperty = BindableProperty.Create(
		nameof(CopyCommand),
		typeof(ICommand),
		typeof(ContactRevealView));

	public ContactRevealView()
	{
		InitializeComponent();
	}

	public bool IsLocked
	{
		get => (bool)GetValue(IsLockedProperty);
		set => SetValue(IsLockedProperty, value);
	}

	public bool IsRevealed
	{
		get => (bool)GetValue(IsRevealedProperty);
		set => SetValue(IsRevealedProperty, value);
	}

	public bool ShowLoadAction
	{
		get => (bool)GetValue(ShowLoadActionProperty);
		set => SetValue(ShowLoadActionProperty, value);
	}

	public bool CanLoad
	{
		get => (bool)GetValue(CanLoadProperty);
		set => SetValue(CanLoadProperty, value);
	}

	public string LoadActionLabel
	{
		get => (string)GetValue(LoadActionLabelProperty);
		set => SetValue(LoadActionLabelProperty, value);
	}

	public IEnumerable? ContactRows
	{
		get => (IEnumerable?)GetValue(ContactRowsProperty);
		set => SetValue(ContactRowsProperty, value);
	}

	public ICommand? LoadCommand
	{
		get => (ICommand?)GetValue(LoadCommandProperty);
		set => SetValue(LoadCommandProperty, value);
	}

	public object? LoadCommandParameter
	{
		get => GetValue(LoadCommandParameterProperty);
		set => SetValue(LoadCommandParameterProperty, value);
	}

	public ICommand? OpenCommand
	{
		get => (ICommand?)GetValue(OpenCommandProperty);
		set => SetValue(OpenCommandProperty, value);
	}

	public ICommand? CopyCommand
	{
		get => (ICommand?)GetValue(CopyCommandProperty);
		set => SetValue(CopyCommandProperty, value);
	}
}
