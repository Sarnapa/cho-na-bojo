using Microsoft.Extensions.DependencyInjection;

namespace ChoNaBojo.App.Services.Navigation;

/// <summary>
/// Resolves the root pages from DI (not <c>new</c>-ed directly) so this seam keeps working as
/// later phases give <see cref="Views.LoginPage"/> constructor dependencies (a ViewModel) and
/// register it accordingly.
/// </summary>
public class NavigationRootService : INavigationRootService
{
	#region Private fields
	private readonly IServiceProvider _serviceProvider;
	#endregion

	#region Constructors
	public NavigationRootService(IServiceProvider serviceProvider)
	{
		_serviceProvider = serviceProvider;
	}
	#endregion

	#region Public methods
	public void SetAuthRoot()
	{
		SetRoot(() => new NavigationPage(_serviceProvider.GetRequiredService<Views.LoginPage>()));
	}

	public void SetAppRoot()
	{
		SetRoot(() => _serviceProvider.GetRequiredService<AppShell>());
	}
	#endregion

	#region Private methods
	private static void SetRoot(Func<Page> createRoot)
	{
		void Apply()
		{
			if (Application.Current?.Windows.Count > 0)
			{
				Application.Current.Windows[0].Page = createRoot();
			}
		}

		if (MainThread.IsMainThread)
		{
			Apply();
		}
		else
		{
			MainThread.BeginInvokeOnMainThread(Apply);
		}
	}
	#endregion
}
