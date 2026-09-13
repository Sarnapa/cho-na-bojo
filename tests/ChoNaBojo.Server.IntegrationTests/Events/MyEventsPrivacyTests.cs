using System.Net;
using System.Text.Json;
using ChoNaBojo.Server.IntegrationTests.Infrastructure;

namespace ChoNaBojo.Server.IntegrationTests.Events;

[Collection(IntegrationTestCollection.Name)]
public sealed class MyEventsPrivacyTests(PostgisFixture fixture) :
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
	[InlineData("organizer")]
	[InlineData("accepted")]
	[InlineData("accepted-peer")]
	[InlineData("pending")]
	[InlineData("rejected")]
	[InlineData("removed")]
	[InlineData("left")]
	[InlineData("stranger")]
	[InlineData("foreign-organizer")]
	[InlineData("foreign-requester")]
	public async Task MyEvents_ReturnsOnlyCallersRelationships(string role)
	{
		const string route = "/api/me/events";
		using HttpClient client = _scenario.CreateClient(role);
		using HttpResponseMessage response = await client.GetAsync(route);
		string body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		using JsonDocument document = JsonDocument.Parse(body);
		Guid[] organizedIds = document.RootElement
			.GetProperty("organizedEvents")
			.EnumerateArray()
			.Select(item => item.GetProperty("eventId").GetGuid())
			.Order()
			.ToArray();
		Guid[] requestedIds = document.RootElement
			.GetProperty("requestedEvents")
			.EnumerateArray()
			.Select(item => item.GetProperty("eventId").GetGuid())
			.Order()
			.ToArray();

		Assert.Equal(ExpectedOrganized(role), organizedIds);
		Assert.Equal(ExpectedRequested(role), requestedIds);
		JsonPrivacyAssertions.DoesNotContainKeys(
			route,
			role,
			body,
			"userId",
			"loginEmail",
			"contactPhone",
			"contactEmail",
			"communicatorPlatform",
			"communicatorHandle",
			"passwordHash",
			"token",
			"refreshToken");
		JsonPrivacyAssertions.DoesNotContainMarkers(
			route,
			role,
			body,
			_scenario.AllContactMarkers.Concat(
				_scenario.Identities.Values.SelectMany(identity =>
				new PrivacyMarker[]
				{
					new PrivacyMarker(
						$"{identity.Role} login email",
						identity.User.LoginEmail),
					new PrivacyMarker(
						$"{identity.Role} password hash",
						identity.User.PasswordHash),
					new PrivacyMarker(
						$"{identity.Role} user id",
						identity.UserId.ToString())
				})));

		HashSet<Guid> permittedRequestIds = PermittedRequestIds(role);
		foreach (TestIdentity identity in _scenario.Identities.Values)
		{
			if (!permittedRequestIds.Contains(identity.RequestId))
			{
				Assert.DoesNotContain(
					identity.RequestId.ToString(),
					body,
					StringComparison.OrdinalIgnoreCase);
			}
		}
	}
	#endregion

	#region Private methods
	private static Guid[] ExpectedOrganized(string role)
	{
		return role switch
		{
			"organizer" =>
			[
				EventScenario.ActiveEventId,
				EventScenario.ClosedEventId,
				EventScenario.CancelledEventId
			],
			"foreign-organizer" => [EventScenario.ForeignEventId],
			_ => []
		};
	}

	private static Guid[] ExpectedRequested(string role)
	{
		return role switch
		{
			"accepted" =>
			[
				EventScenario.ActiveEventId,
				EventScenario.ClosedEventId,
				EventScenario.CancelledEventId
			],
			"accepted-peer"
				or "pending"
				or "rejected"
				or "removed"
				or "left" => [EventScenario.ActiveEventId],
			"foreign-requester" => [EventScenario.ForeignEventId],
			_ => []
		};
	}

	private HashSet<Guid> PermittedRequestIds(string role)
	{
		var ids = new HashSet<Guid>();
		if (role is "accepted"
			or "accepted-peer"
			or "pending"
			or "rejected"
			or "removed"
			or "left"
			or "foreign-requester")
		{
			ids.Add(_scenario[role].RequestId);
		}

		if (role == "accepted")
		{
			ids.Add(EventScenario.ClosedAcceptedRequestId);
			ids.Add(EventScenario.CancelledAcceptedRequestId);
		}

		return ids;
	}
	#endregion
}
