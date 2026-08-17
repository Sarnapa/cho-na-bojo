using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChoNaBojo.Contracts.DTOs;

namespace ChoNaBojo.App.Services.Auth;

public class AuthTokenClient : IAuthTokenClient
{
	#region Private fields
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	private readonly HttpClient _httpClient;
	#endregion

	#region Constructors
	public AuthTokenClient(IHttpClientFactory httpClientFactory)
	{
		_httpClient = httpClientFactory.CreateClient("ChoNaBojoAuth");
	}
	#endregion

	#region Public methods
	public async Task<RefreshOutcome> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
	{
		try
		{
			using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
				"/auth/refresh",
				new RefreshRequest(refreshToken),
				cancellationToken);

			if (response.IsSuccessStatusCode)
			{
				var body = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions, cancellationToken);
				return body is null ? RefreshOutcome.Transient() : RefreshOutcome.Success(body);
			}

			if (response.StatusCode == HttpStatusCode.Conflict)
			{
				return RefreshOutcome.RetryInProgress();
			}

			if (response.StatusCode == HttpStatusCode.Unauthorized)
			{
				return RefreshOutcome.Invalid();
			}

			// 429 (auth rate limit), 5xx, and anything else unexpected are transient — never
			// sign the user out for these; the caller keeps the session and surfaces a retry.
			return RefreshOutcome.Transient();
		}
		catch (HttpRequestException)
		{
			return RefreshOutcome.Transient();
		}
		catch (TaskCanceledException)
		{
			return RefreshOutcome.Transient();
		}
	}

	public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
	{
		try
		{
			await _httpClient.PostAsJsonAsync("/auth/logout", new RefreshRequest(refreshToken), cancellationToken);
		}
		catch (HttpRequestException)
		{
			// Best-effort server-side revocation; local sign-out already happened regardless.
		}
		catch (TaskCanceledException)
		{
		}
	}
	#endregion
}
