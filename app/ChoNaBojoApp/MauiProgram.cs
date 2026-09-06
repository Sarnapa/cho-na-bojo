using CommunityToolkit.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UraniumUI;
using ChoNaBojo.App.Services;
using ChoNaBojo.App.Services.Auth;
using ChoNaBojo.App.Services.Events;
using ChoNaBojo.App.Services.Feedback;
using ChoNaBojo.App.Services.Geocoding;
using ChoNaBojo.App.Services.Navigation;
using ChoNaBojo.App.Services.Venues;
using ChoNaBojo.App.ViewModels;
using ChoNaBojo.App.Views;
#if ANDROID
using ChoNaBojo.App.Platforms.Android;
#endif

namespace ChoNaBojo.App
{
	public static class MauiProgram
	{
		#region Public static methods
		public static MauiApp CreateMauiApp()
		{
			var builder = MauiApp.CreateBuilder();
			builder
				.UseMauiApp<App>()
				.UseUraniumUI()
				.UseUraniumUIMaterial()
				.UseMauiCommunityToolkit()
				// Unconditional across all TFMs: Windows has a real (Azure Maps-backed) handler
				// that renders blank without a token rather than failing to build or crashing.
				.UseMauiMaps()
				.ConfigureMauiHandlers(handlers =>
				{
					handlers.AddHandler<Views.Maps.VenueMap, Views.Maps.VenueMapHandler>();
#if ANDROID
					handlers.AddHandler<Views.Maps.VenuePin, Platforms.Android.VenuePinHandler>();
#endif
				})
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

				// Chosen bound rather than the 100 s default: an in-flight create blocks Back and
				// the map's Create button, so a black-holed connection must resolve to the typed
				// network result (safe retry via ClientRequestId) in a tolerable time.
				client.Timeout = TimeSpan.FromSeconds(30);
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
			builder.Services.AddSingleton<IFeedbackService, FeedbackService>();
#if ANDROID
			builder.Services.AddSingleton<IAddressSearchService, AndroidAddressSearchService>();
#else
			builder.Services.AddSingleton<IAddressSearchService, AddressSearchService>();
#endif
			builder.Services.AddSingleton<SessionExpiryCoordinator>();
			builder.Services.AddSingleton<IVenueCatalog, VenueCatalog>();

			builder.Services.AddTransient<AppShell>();
			builder.Services.AddTransient<LoadingPage>();
			builder.Services.AddTransient<LoginPage>();
			builder.Services.AddTransient<RegisterPage>();
			builder.Services.AddTransient<MapPage>();
			builder.Services.AddTransient<AddressSearchPage>();
			builder.Services.AddTransient<CreateEventPage>();
			builder.Services.AddTransient<EventDetailPage>();

			builder.Services.AddTransient<LoginViewModel>();
			builder.Services.AddTransient<RegisterViewModel>();
			builder.Services.AddTransient<MapViewModel>();
			builder.Services.AddTransient<AddressSearchViewModel>();
			builder.Services.AddTransient<CreateEventViewModel>();
			builder.Services.AddTransient<EventDetailViewModel>();

#if DEBUG
			builder.Logging.AddDebug();
#endif

			MauiApp app = builder.Build();

			// Eagerly resolve so the SessionExpired subscription is live before any page appears
			// — it is otherwise never resolved by DI (nothing else depends on it).
			app.Services.GetRequiredService<SessionExpiryCoordinator>();

			return app;
		}
		#endregion

		#region Private static methods
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
		#endregion
	}
}
