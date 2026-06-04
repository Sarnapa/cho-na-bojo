namespace ChoNaBojo.App.Services;

public class ApiService : IApiService
{
	private readonly HttpClient _httpClient;

  public ApiService(IHttpClientFactory httpClientFactory)
  {
    _httpClient = httpClientFactory.CreateClient("ChoNaBojoApi");
  }

  public async Task<bool> CheckHealthAsync()
  {
    try
    {
			var response = await _httpClient.GetAsync("/health");
      return response.IsSuccessStatusCode;
    }
    catch (HttpRequestException)
    {
      return false;
    }
  }
}
