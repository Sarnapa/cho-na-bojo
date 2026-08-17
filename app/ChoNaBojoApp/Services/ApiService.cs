using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChoNaBojo.App.Services.Auth;
using ChoNaBojo.Contracts.DTOs;

namespace ChoNaBojo.App.Services;

public class ApiService : IApiService
{
	#region Private fields
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	private readonly HttpClient _httpClient;
	#endregion

	#region Constructors
	public ApiService(IHttpClientFactory httpClientFactory)
  {
    _httpClient = httpClientFactory.CreateClient("ChoNaBojoApi");
  }
	#endregion

	#region Public methods
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

	public Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
	{
		return PostAuthAsync("/auth/register", request, cancellationToken);
	}

	public Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
	{
		return PostAuthAsync("/auth/login", request, cancellationToken);
	}

	public async Task<CurrentUserResult> GetCurrentUserAsync(CancellationToken cancellationToken)
	{
		try
		{
			using HttpResponseMessage response = await _httpClient.GetAsync("/auth/me", cancellationToken);

			if (response.IsSuccessStatusCode)
			{
				var body = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(JsonOptions, cancellationToken);
				return body is null ? CurrentUserResult.Unknown() : CurrentUserResult.Success(body);
			}

			if (response.StatusCode == HttpStatusCode.Unauthorized)
			{
				return CurrentUserResult.Unauthorized();
			}

			return CurrentUserResult.Unknown();
		}
		catch (HttpRequestException)
		{
			return CurrentUserResult.Network();
		}
		catch (TaskCanceledException)
		{
			return CurrentUserResult.Network();
		}
	}
	#endregion

	#region Private methods
	private async Task<AuthResult> PostAuthAsync<TRequest>(string path, TRequest request, CancellationToken cancellationToken)
	{
		try
		{
			using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(path, request, cancellationToken);

			if (response.IsSuccessStatusCode)
			{
				var body = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions, cancellationToken);
				return body is null ? AuthResult.Unknown() : AuthResult.Success(body);
			}

			switch (response.StatusCode)
			{
				case HttpStatusCode.BadRequest:
					var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>(JsonOptions, cancellationToken);
					return problem is null
						? AuthResult.Unknown()
						: AuthResult.ValidationFailed(problem.Errors);

				case HttpStatusCode.Unauthorized:
					return AuthResult.Unauthorized();

				case HttpStatusCode.Conflict:
					var conflict = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
					string message = conflict.TryGetProperty("message", out var messageElement)
						? messageElement.GetString() ?? "Conflict."
						: "Conflict.";
					return AuthResult.Conflict(message);

				default:
					return AuthResult.Unknown();
			}
		}
		catch (HttpRequestException)
		{
			return AuthResult.Network();
		}
		catch (TaskCanceledException)
		{
			return AuthResult.Network();
		}
	}
	#endregion
}
