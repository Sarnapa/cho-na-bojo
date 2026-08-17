using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using UraniumUI;
using ChoNaBojo.App.Services;
using ChoNaBojo.App.Services.Auth;
using ChoNaBojo.App.Services.Navigation;

namespace ChoNaBojo.App
{
	public static class MauiProgram
	{
		public static MauiApp CreateMauiApp()
		{
			var builder = MauiApp.CreateBuilder();
			builder
				.UseMauiApp<App>()
				.UseUraniumUI()
				.UseUraniumUIMaterial()
				.UseMauiCommunityToolkit()
				.ConfigureFonts(fonts =>
				{
					fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
					fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
					fonts.AddMaterialSymbolsFonts();
				});

			// Protected/business calls: bearer attach + transparent refresh via
			// AuthenticatingHttpMessageHandler.
			builder.Services.AddHttpClient("ChoNaBojoApi", client =>
			{
				client.BaseAddress = new Uri(GetApiBaseAddress());
			}).AddHttpMessageHandler<AuthenticatingHttpMessageHandler>();

			// Un-handled client for /auth/refresh and /auth/logout: must never itself be
			// refreshed or recurse through AuthenticatingHttpMessageHandler.
			builder.Services.AddHttpClient("ChoNaBojoAuth", client =>
			{
				client.BaseAddress = new Uri(GetApiBaseAddress());
			});

			builder.Services.AddTransient<AuthenticatingHttpMessageHandler>();

			builder.Services.AddSingleton<ITokenStore, TokenStore>();
			builder.Services.AddSingleton<ISessionService, SessionService>();
			builder.Services.AddSingleton<IAuthTokenClient, AuthTokenClient>();
			builder.Services.AddSingleton<INavigationRootService, NavigationRootService>();
			builder.Services.AddSingleton<IApiService, ApiService>();

			builder.Services.AddTransient<MainPage>();
			builder.Services.AddTransient<AppShell>();
			builder.Services.AddTransient<Views.LoginPage>();

#if DEBUG
			builder.Logging.AddDebug();
#endif

			return builder.Build();
		}

		// Shared by both named clients so the dev/prod split is defined once.
		private static string GetApiBaseAddress()
		{
#if DEBUG
			return DeviceInfo.Platform == DevicePlatform.Android
				? "http://10.0.2.2:5100"
				: "http://localhost:5100";
#else
			return "https://cho-na-bojo-production.up.railway.app";
#endif
		}
	}
}
