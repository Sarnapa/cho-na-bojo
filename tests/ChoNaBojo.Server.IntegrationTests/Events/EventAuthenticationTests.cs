using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ChoNaBojo.Server.IntegrationTests.Infrastructure;

namespace ChoNaBojo.Server.IntegrationTests.Events;

[Collection(IntegrationTestCollection.Name)]
public sealed class EventAuthenticationTests(PostgisFixture fixture) :
	IAsyncLifetime
{
	#region Private fields
	private EventScenario _scenario = null!;
	#endregion

	#region IAsyncLifetime implementation
	public async Task InitializeAsync()
	{
		_scenario = await new EventScenarioBuilder(fixture).BuildAsync();
	}

	public Task DisposeAsync()
	{
		return Task.CompletedTask;
	}
	#endregion

	#region Test methods
	[Theory]
	[MemberData(nameof(ProtectedRouteCases))]
	public async Task EveryEventRoute_RejectsMissingOrInvalidIdentity(
		string route,
		string method,
		string tokenKind)
	{
		using HttpClient client = fixture.ApiFactory.CreateHttpsClient();
		string? token = CreateToken(tokenKind);
		if (token is not null)
		{
			client.DefaultRequestHeaders.Authorization =
				new AuthenticationHeaderValue("Bearer", token);
		}

		using var request = new HttpRequestMessage(
			new HttpMethod(method),
			route);
		if (method == HttpMethod.Post.Method)
		{
			request.Content = new StringContent(
				"{}",
				Encoding.UTF8,
				"application/json");
		}

		using HttpResponseMessage response = await client.SendAsync(request);
		string body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		JsonPrivacyAssertions.DoesNotContainMarkers(
			route,
			tokenKind,
			body,
			_scenario.AllIdentityMarkers);
		Assert.DoesNotContain(
			EventScenario.ActiveEventId.ToString(),
			body,
			StringComparison.OrdinalIgnoreCase);
	}
	#endregion

	#region Public methods
	public static IEnumerable<object[]> ProtectedRouteCases()
	{
		string eventId = EventScenario.ActiveEventId.ToString();
		string requestId =
			Guid.Parse("20000000-0000-0000-0000-000000000004").ToString();
		(string Method, string Route)[] routes =
		[
			("POST", "/api/events"),
			("GET", $"/api/venues/{EventScenario.VenueId}/events"),
			("POST", $"/api/events/{eventId}/join-requests"),
			("POST", $"/api/events/{eventId}/join-requests/{requestId}/accept"),
			("POST", $"/api/events/{eventId}/join-requests/{requestId}/reject"),
			("POST", $"/api/events/{eventId}/join-requests/{requestId}/remove"),
			("POST", $"/api/events/{eventId}/join-requests/mine/leave"),
			("POST", $"/api/events/{eventId}/cancel"),
			("GET", "/api/me/events"),
			("GET", $"/api/events/{eventId}/join-requests"),
			("GET", $"/api/events/{eventId}/contacts")
		];
		string[] tokenKinds =
		[
			"missing",
			"invalid-signature",
			"expired",
			"missing-subject",
			"non-guid-subject"
		];

		foreach ((string method, string route) in routes)
		{
			foreach (string tokenKind in tokenKinds)
			{
				yield return [route, method, tokenKind];
			}
		}
	}
	#endregion

	#region Private methods
	private string? CreateToken(string tokenKind)
	{
		Guid userId = _scenario["organizer"].UserId;
		return tokenKind switch
		{
			"missing" => null,
			"invalid-signature" =>
				TestJwtFactory.CreateTokenWithInvalidSignature(userId),
			"expired" => TestJwtFactory.CreateExpiredToken(userId),
			"missing-subject" =>
				TestJwtFactory.CreateTokenWithoutStableIdentity(),
			"non-guid-subject" =>
				TestJwtFactory.CreateTokenWithInvalidStableIdentity(),
			_ => throw new ArgumentOutOfRangeException(nameof(tokenKind))
		};
	}
	#endregion
}
