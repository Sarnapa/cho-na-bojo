using System.Net;
using System.Text.Json;
using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Contracts.Enums;
using ChoNaBojo.Server.IntegrationTests.Infrastructure;

namespace ChoNaBojo.Server.IntegrationTests.Events;

[Collection(IntegrationTestCollection.Name)]
public sealed class JoinRequestQueuePrivacyTests(PostgisFixture fixture) :
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
	[Fact]
	public async Task OrganizerQueue_ExposesPseudonymousWorkflowDataOnly()
	{
		string route =
			$"/api/events/{EventScenario.ActiveEventId}/join-requests";
		using HttpClient client = _scenario.CreateClient("organizer");
		using HttpResponseMessage response = await client.GetAsync(route);
		string body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		using JsonDocument document = JsonDocument.Parse(body);
		JsonElement[] items = document.RootElement.EnumerateArray().ToArray();
		Assert.Equal(6, items.Length);

		Guid[] expectedRequestIds =
		[
			_scenario["accepted"].RequestId,
			_scenario["accepted-peer"].RequestId,
			_scenario["pending"].RequestId,
			_scenario["rejected"].RequestId,
			_scenario["removed"].RequestId,
			_scenario["left"].RequestId
		];
		Assert.Equal(
			expectedRequestIds.Order(),
			items.Select(item => item.GetProperty("requestId").GetGuid()).Order());
		Assert.Equal(
			[
				(int)EventJoinRequestStatus.Pending,
				(int)EventJoinRequestStatus.Accepted,
				(int)EventJoinRequestStatus.Rejected,
				(int)EventJoinRequestStatus.Left,
				(int)EventJoinRequestStatus.Removed
			],
			items
				.Select(item => item.GetProperty("status").GetInt32())
				.Distinct()
				.Order());
		Assert.All(
			items,
			item => Assert.StartsWith(
				"Requester ",
				item.GetProperty("requesterDisplayKey").GetString()));

		JsonPrivacyAssertions.DoesNotContainKeys(
			route,
			"organizer",
			body,
			"userId",
			"loginEmail",
			"contactPhone",
			"contactEmail",
			"communicatorPlatform",
			"communicatorHandle",
			"passwordHash",
			"token");
		JsonPrivacyAssertions.DoesNotContainMarkers(
			route,
			"organizer",
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
	}

	[Theory]
	[InlineData("accepted", "active")]
	[InlineData("accepted-peer", "active")]
	[InlineData("pending", "active")]
	[InlineData("rejected", "active")]
	[InlineData("removed", "active")]
	[InlineData("left", "active")]
	[InlineData("stranger", "active")]
	[InlineData("foreign-organizer", "active")]
	[InlineData("organizer", "foreign")]
	[InlineData("organizer", "missing")]
	public async Task QueueProbes_UseCanonicalNotFoundCloak(
		string role,
		string target)
	{
		Guid eventId = target switch
		{
			"active" => EventScenario.ActiveEventId,
			"foreign" => EventScenario.ForeignEventId,
			"missing" => EventScenario.MissingEventId,
			_ => throw new ArgumentOutOfRangeException(nameof(target))
		};
		using HttpClient client = _scenario.CreateClient(role);
		using HttpResponseMessage baselineResponse = await client.GetAsync(
			$"/api/events/{EventScenario.MissingEventId}/join-requests");
		EventConflictResponse baseline =
			await HttpTestAssertions.ReadConflictAsync(baselineResponse);

		using HttpResponseMessage response = await client.GetAsync(
			$"/api/events/{eventId}/join-requests");

		await HttpTestAssertions.AssertCanonicalNotFoundAsync(
			response,
			baseline);
	}
	#endregion
}
