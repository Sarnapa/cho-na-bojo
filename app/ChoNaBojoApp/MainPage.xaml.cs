using ChoNaBojo.App.Services;

namespace ChoNaBojo.App
{
	public partial class MainPage: ContentPage
	{
		private readonly IApiService _apiService;
		private int count = 0;

		public MainPage(IApiService apiService)
		{
			InitializeComponent();
			_apiService = apiService;
		}

		protected override async void OnAppearing()
		{
			base.OnAppearing();
			var isHealthy = await _apiService.CheckHealthAsync();
			if (!isHealthy)
			{
				await DisplayAlertAsync("Offline", "Unable to reach the server. Some features may be unavailable.", "OK");
			}
		}

		private void OnCounterClicked(object? sender, EventArgs e)
		{
			count++;

			if (count == 1)
				CounterBtn.Text = $"Clicked {count} time";
			else
				CounterBtn.Text = $"Clicked {count} times";

			SemanticScreenReader.Announce(CounterBtn.Text);
		}
	}
}
