using System.Net;
using System.Text.Json;
using ChoNaBojo.Contracts.DTOs;

namespace ChoNaBojo.Server.IntegrationTests.Infrastructure;

internal static class HttpTestAssertions
{
	#region Private fields
	private static readonly JsonSerializerOptions WebJson =
		new(JsonSerializerDefaults.Web);
	#endregion

	#region Public methods
	public static async Task<string> ReadBodyAsync(
		HttpResponseMessage response)
	{
		return await response.Content.ReadAsStringAsync();
	}

	public static async Task<EventConflictResponse> ReadConflictAsync(
		HttpResponseMessage response)
	{
		string body = await ReadBodyAsync(response);
		EventConflictResponse? conflict =
			JsonSerializer.Deserialize<EventConflictResponse>(body, WebJson);
		Assert.NotNull(conflict);
		return conflict;
	}

	public static async Task AssertCanonicalNotFoundAsync(
		HttpResponseMessage response,
		EventConflictResponse expected)
	{
		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
		Assert.Equal(expected, await ReadConflictAsync(response));
	}
	#endregion
}
