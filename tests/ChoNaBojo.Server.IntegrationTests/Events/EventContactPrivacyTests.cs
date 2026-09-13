using System.Net;
using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Server.IntegrationTests.Infrastructure;

namespace ChoNaBojo.Server.IntegrationTests.Events;

[Collection(IntegrationTestCollection.Name)]
public sealed class EventContactPrivacyTests(PostgisFixture fixture) :
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
	[InlineData("organizer", "active")]
	[InlineData("organizer", "closed")]
	public async Task Organizer_ReceivesAcceptedParticipantsOnly(
		string role,
		string lifecycle)
	{
		Guid eventId = lifecycle == "active"
			? EventScenario.ActiveEventId
			: EventScenario.ClosedEventId;
		string route = $"/api/events/{eventId}/contacts";
		using HttpClient client = _scenario.CreateClient(role);
		using HttpResponseMessage response = await client.GetAsync(route);
		string body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		TestIdentity accepted = _scenario["accepted"];
		JsonPrivacyAssertions.ContainsMarker(
			route,
			role,
			body,
			new PrivacyMarker("accepted user id", accepted.UserId.ToString()));
		JsonPrivacyAssertions.ContainsMarker(
			route,
			role,
			body,
			accepted.ContactMarkers.First());

		if (lifecycle == "active")
		{
			TestIdentity acceptedPeer = _scenario["accepted-peer"];
			JsonPrivacyAssertions.ContainsMarker(
				route,
				role,
				body,
				new PrivacyMarker(
					"accepted peer user id",
					acceptedPeer.UserId.ToString()));
			JsonPrivacyAssertions.ContainsMarker(
				route,
				role,
				body,
				acceptedPeer.ContactMarkers.First());
			Assert.Contains(
				accepted.RequestId.ToString(),
				body,
				StringComparison.OrdinalIgnoreCase);
			Assert.Contains(
				acceptedPeer.RequestId.ToString(),
				body,
				StringComparison.OrdinalIgnoreCase);
		}
		else
		{
			Assert.Contains(
				EventScenario.ClosedAcceptedRequestId.ToString(),
				body,
				StringComparison.OrdinalIgnoreCase);
		}

		string[] deniedRoles =
		[
			"pending",
			"rejected",
			"removed",
			"left",
			"stranger",
			"foreign-requester",
			"organizer"
		];
		JsonPrivacyAssertions.DoesNotContainMarkers(
			route,
			role,
			body,
			deniedRoles.SelectMany(deniedRole =>
				_scenario[deniedRole].ContactMarkers));
		JsonPrivacyAssertions.DoesNotContainKeys(
			route,
			role,
			body,
			"loginEmail",
			"passwordHash",
			"token",
			"refreshToken");
	}

	[Theory]
	[InlineData("accepted")]
	[InlineData("accepted-peer")]
	public async Task AcceptedParticipant_ReceivesOrganizerButNeverPeers(
		string role)
	{
		string route =
			$"/api/events/{EventScenario.ActiveEventId}/contacts";
		using HttpClient client = _scenario.CreateClient(role);
		using HttpResponseMessage response = await client.GetAsync(route);
		string body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		TestIdentity organizer = _scenario["organizer"];
		JsonPrivacyAssertions.ContainsMarker(
			route,
			role,
			body,
			new PrivacyMarker("organizer user id", organizer.UserId.ToString()));
		foreach (PrivacyMarker marker in organizer.ContactMarkers)
		{
			JsonPrivacyAssertions.ContainsMarker(route, role, body, marker);
		}

		JsonPrivacyAssertions.DoesNotContainMarkers(
			route,
			role,
			body,
			_scenario["accepted"].ContactMarkers
				.Concat(_scenario["accepted-peer"].ContactMarkers));
		Assert.DoesNotContain(
			_scenario["accepted"].UserId.ToString(),
			body,
			StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain(
			_scenario["accepted-peer"].UserId.ToString(),
			body,
			StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain(
			_scenario["accepted"].RequestId.ToString(),
			body,
			StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain(
			_scenario["accepted-peer"].RequestId.ToString(),
			body,
			StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task AcceptedParticipant_RetainsOrganizerContactAfterClose()
	{
		string route =
			$"/api/events/{EventScenario.ClosedEventId}/contacts";
		using HttpClient client = _scenario.CreateClient("accepted");
		using HttpResponseMessage response = await client.GetAsync(route);
		string body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		foreach (PrivacyMarker marker in _scenario["organizer"].ContactMarkers)
		{
			JsonPrivacyAssertions.ContainsMarker(
				route,
				"accepted",
				body,
				marker);
		}
	}

	[Theory]
	[InlineData("pending", "active")]
	[InlineData("rejected", "active")]
	[InlineData("removed", "active")]
	[InlineData("left", "active")]
	[InlineData("stranger", "active")]
	[InlineData("foreign-requester", "active")]
	[InlineData("organizer", "foreign")]
	[InlineData("organizer", "missing")]
	[InlineData("organizer", "cancelled")]
	[InlineData("accepted", "cancelled")]
	public async Task DeniedContacts_UseCanonicalNotFoundCloak(
		string role,
		string target)
	{
		Guid eventId = target switch
		{
			"active" => EventScenario.ActiveEventId,
			"foreign" => EventScenario.ForeignEventId,
			"missing" => EventScenario.MissingEventId,
			"cancelled" => EventScenario.CancelledEventId,
			_ => throw new ArgumentOutOfRangeException(nameof(target))
		};
		using HttpClient client = _scenario.CreateClient(role);
		using HttpResponseMessage baselineResponse = await client.GetAsync(
			$"/api/events/{EventScenario.MissingEventId}/contacts");
		EventConflictResponse baseline =
			await HttpTestAssertions.ReadConflictAsync(baselineResponse);

		using HttpResponseMessage response =
			await client.GetAsync($"/api/events/{eventId}/contacts");

		await HttpTestAssertions.AssertCanonicalNotFoundAsync(
			response,
			baseline);
	}
	#endregion
}
