using System.Net;
using System.Text.Json;
using ChoNaBojo.Contracts.Enums;
using ChoNaBojo.Server.IntegrationTests.Infrastructure;

namespace ChoNaBojo.Server.IntegrationTests.Events;

[Collection(IntegrationTestCollection.Name)]
public sealed class VenueEventPrivacyTests(PostgisFixture fixture) :
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
	[InlineData("organizer", true, null)]
	[InlineData("accepted", false, (int)EventJoinRequestStatus.Accepted)]
	[InlineData("accepted-peer", false, (int)EventJoinRequestStatus.Accepted)]
	[InlineData("pending", false, (int)EventJoinRequestStatus.Pending)]
	[InlineData("rejected", false, (int)EventJoinRequestStatus.Rejected)]
	[InlineData("removed", false, (int)EventJoinRequestStatus.Removed)]
	[InlineData("left", false, (int)EventJoinRequestStatus.Left)]
	[InlineData("stranger", false, null)]
	[InlineData("foreign-organizer", false, null)]
	[InlineData("foreign-requester", false, null)]
	public async Task VenueListing_ExposesOnlyCallerRelativeState(
		string role,
		bool expectedOrganizer,
		int? expectedRequestStatus)
	{
		string route = $"/api/venues/{EventScenario.VenueId}/events";
		using HttpClient client = _scenario.CreateClient(role);
		using HttpResponseMessage response = await client.GetAsync(route);
		string body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		using JsonDocument document = JsonDocument.Parse(body);
		JsonElement item = Assert.Single(document.RootElement.EnumerateArray());
		Assert.Equal(
			EventScenario.ActiveEventId,
			item.GetProperty("eventId").GetGuid());
		Assert.Equal(
			"Privacy matrix football",
			item.GetProperty("title").GetString());
		Assert.Equal(3, item.GetProperty("participantCount").GetInt32());
		Assert.Equal(expectedOrganizer, item.GetProperty("isOrganizer").GetBoolean());
		JsonElement requestStatus =
			item.GetProperty("currentUserRequestStatus");
		if (expectedRequestStatus.HasValue)
		{
			Assert.Equal(expectedRequestStatus.Value, requestStatus.GetInt32());
		}
		else
		{
			Assert.Equal(JsonValueKind.Null, requestStatus.ValueKind);
		}

		JsonPrivacyAssertions.DoesNotContainKeys(
			route,
			role,
			body,
			"requestId",
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
			_scenario.AllIdentityMarkers);
	}
	#endregion
}
