using ChoNaBojo.App.Services.Auth;
using ChoNaBojo.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace ChoNaBojo.App.Services.Push;

public sealed class PushNavigationRouter : IPushNavigationRouter
{
	#region Private constants
	private const string MyEventsRoute = "//MyEventsPage";
	#endregion

	#region Private fields
	private readonly IServiceProvider _serviceProvider;
	private readonly Lock _pendingLock = new();
	private readonly SemaphoreSlim _consumeLock = new(1, 1);
	private PushNotificationPayload? _pending;
	#endregion

	#region Constructors
	public PushNavigationRouter(IServiceProvider serviceProvider)
	{
		_serviceProvider = serviceProvider;
	}
	#endregion

	#region Public methods
	public bool TryEnqueue(PushNotificationPayload payload)
	{
		ArgumentNullException.ThrowIfNull(payload);
		if (!payload.IsValid)
		{
			return false;
		}

		lock (_pendingLock)
		{
			_pending = payload;
		}

		return true;
	}

	public Task ConsumePendingAsync()
	{
		return MainThread.InvokeOnMainThreadAsync(ConsumePendingOnMainThreadAsync);
	}

	public Task RefreshVisibleMyEventsAsync()
	{
		return MainThread.InvokeOnMainThreadAsync(async () =>
		{
			if (Shell.Current?.CurrentPage is MyEventsPage page)
			{
				await page.RefreshFromPushAsync();
			}
		});
	}

	public void Clear()
	{
		lock (_pendingLock)
		{
			_pending = null;
		}
	}
	#endregion

	#region Private methods
	private async Task ConsumePendingOnMainThreadAsync()
	{
		await _consumeLock.WaitAsync();
		try
		{
			ISessionService sessionService =
				_serviceProvider.GetRequiredService<ISessionService>();
			AppShell? shell = Shell.Current as AppShell;
			if (!sessionService.IsAuthenticated || shell is null)
			{
				return;
			}

			if (shell.Handler is null)
			{
				shell.Loaded -= OnShellLoaded;
				shell.Loaded += OnShellLoaded;
				return;
			}

			shell.Loaded -= OnShellLoaded;
			PushNotificationPayload? pending;
			lock (_pendingLock)
			{
				pending = _pending;
			}

			if (pending is null)
			{
				return;
			}

			bool wasAlreadyVisible = shell.CurrentPage is MyEventsPage;
			await shell.GoToAsync(MyEventsRoute);
			if (wasAlreadyVisible && shell.CurrentPage is MyEventsPage page)
			{
				await page.RefreshFromPushAsync();
			}

			lock (_pendingLock)
			{
				if (ReferenceEquals(_pending, pending))
				{
					_pending = null;
				}
			}
		}
		finally
		{
			_consumeLock.Release();
		}
	}

	private void OnShellLoaded(object? sender, EventArgs e)
	{
		if (sender is AppShell shell)
		{
			shell.Loaded -= OnShellLoaded;
		}

		_ = ConsumePendingAsync();
	}
	#endregion
}
