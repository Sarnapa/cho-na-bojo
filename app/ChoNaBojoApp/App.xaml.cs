using Microsoft.Extensions.DependencyInjection;

namespace ChoNaBojo.App
{
	public partial class App: Application
	{
		private readonly IServiceProvider _serviceProvider;

		public App(IServiceProvider serviceProvider)
		{
			InitializeComponent();
			_serviceProvider = serviceProvider;
		}

		protected override Window CreateWindow(IActivationState? activationState)
		{
			return new Window(_serviceProvider.GetRequiredService<Views.LoadingPage>());
		}
	}
}