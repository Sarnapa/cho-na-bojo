using ChoNaBojo.App.ViewModels;

namespace ChoNaBojo.App.Views;

public partial class MyEventsPage : ContentPage
{
	#region Private fields
	private readonly MyEventsViewModel _viewModel;
	private CancellationTokenSource? _appearingCancellation;
	#endregion

	#region Constructors
	public MyEventsPage(MyEventsViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = viewModel;
	}
	#endregion

	#region Public methods
	public async Task RefreshFromPushAsync()
	{
		Task? currentRefresh = _viewModel.RefreshCommand.ExecutionTask;
		if (currentRefresh is { IsCompleted: false })
		{
			await currentRefresh;
		}

		if (_viewModel.RefreshCommand.CanExecute(null))
		{
			await _viewModel.RefreshCommand.ExecuteAsync(null);
		}
	}
	#endregion

	#region Overrides
	protected override async void OnAppearing()
	{
		base.OnAppearing();

		CancellationTokenSource appearingCancellation = new();
		CancellationTokenSource? previousCancellation =
			Interlocked.Exchange(
				ref _appearingCancellation,
				appearingCancellation);
		previousCancellation?.Cancel();
		previousCancellation?.Dispose();

		try
		{
			Task? currentRefresh = _viewModel.RefreshCommand.ExecutionTask;
			if (currentRefresh is { IsCompleted: false })
			{
				await currentRefresh.WaitAsync(appearingCancellation.Token);
			}

			appearingCancellation.Token.ThrowIfCancellationRequested();
			if (_viewModel.RefreshCommand.CanExecute(null))
			{
				await _viewModel.RefreshCommand.ExecuteAsync(null);
			}
		}
		catch (OperationCanceledException)
			when (appearingCancellation.IsCancellationRequested)
		{
		}
		finally
		{
			if (ReferenceEquals(
				Interlocked.CompareExchange(
					ref _appearingCancellation,
					null,
					appearingCancellation),
				appearingCancellation))
			{
				appearingCancellation.Dispose();
			}
		}
	}

	protected override void OnDisappearing()
	{
		if (!_viewModel.IsConfirmationInProgress)
		{
			CancellationTokenSource? appearingCancellation =
				Interlocked.Exchange(ref _appearingCancellation, null);
			appearingCancellation?.Cancel();
			appearingCancellation?.Dispose();
			_viewModel.Cleanup();
		}

		base.OnDisappearing();
	}
	#endregion
}
