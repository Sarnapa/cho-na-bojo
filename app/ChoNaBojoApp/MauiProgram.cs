using Microsoft.Extensions.Logging;
using UraniumUI;
using UraniumUI.Icons.MaterialSymbols;

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
				.ConfigureFonts(fonts =>
				{
					fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
					fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
					fonts.AddMaterialSymbolsFonts();
				});

			builder.Services.AddHttpClient("ChoNaBojoApi", client =>
			{
#if DEBUG
				client.BaseAddress = new Uri(
					DeviceInfo.Platform == DevicePlatform.Android
						? "http://10.0.2.2:5100"
						: "http://localhost:5100");
#else
				client.BaseAddress = new Uri("https://cho-na-bojo-production.up.railway.app");
#endif
			});

			builder.Services.AddSingleton<Services.IApiService, Services.ApiService>();
			builder.Services.AddTransient<MainPage>();

#if DEBUG
			builder.Logging.AddDebug();
#endif

			return builder.Build();
		}
	}
}
