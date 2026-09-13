using System.Net;
using System.Text.Json;
using ChoNaBojo.Contracts.Consts;
using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Contracts.Enums;
using ChoNaBojo.Server.Data;
using ChoNaBojo.Server.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChoNaBojo.Server.IntegrationTests.Events;

[Collection(IntegrationTestCollection.Name)]
public sealed class LeaveEventAuthorizationTests(PostgisFixture fixture) :
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
	public async Task AcceptedParticipant_CanLeaveAndLosesContactEntitlement()
	{
		string route =
			$"/api/events/{EventScenario.ActiveEventId}/join-requests/mine/leave";
		using HttpClient client = _scenario.CreateClient("accepted");

		using HttpResponseMessage response =
			await client.PostAsync(route, content: null);
		string body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		using JsonDocument document = JsonDocument.Parse(body);
		Assert.Equal(
			(int)EventJoinRequestStatus.Left,
			document.RootElement.GetProperty("status").GetInt32());

		await using AsyncServiceScope scope =
			fixture.ApiFactory.Services.CreateAsyncScope();
		var dbContext =
			scope.ServiceProvider.GetRequiredService<ChoNaBojoContext>();
		var request = await dbContext.EventJoinRequests
			.AsNoTracking()
			.SingleAsync(entity => entity.Id == _scenario["accepted"].RequestId);
		Assert.Equal(EventJoinRequestStatus.Left, request.Status);
		Assert.NotNull(request.UpdatedUtc);
		Assert.Equal(
			1,
			await dbContext.EventJoinRequests.CountAsync(entity =>
				entity.SportsEventId == EventScenario.ActiveEventId
					&& entity.Status == EventJoinRequestStatus.Accepted));

		using HttpResponseMessage contactsResponse = await client.GetAsync(
			$"/api/events/{EventScenario.ActiveEventId}/contacts");
		Assert.Equal(HttpStatusCode.NotFound, contactsResponse.StatusCode);

		using HttpClient organizer = _scenario.CreateClient("organizer");
		string organizerContacts = await organizer.GetStringAsync(
			$"/api/events/{EventScenario.ActiveEventId}/contacts");
		Assert.DoesNotContain(
			_scenario["accepted"].UserId.ToString(),
			organizerContacts,
			StringComparison.OrdinalIgnoreCase);
		Assert.Contains(
			_scenario["accepted-peer"].UserId.ToString(),
			organizerContacts,
			StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData("pending")]
	[InlineData("rejected")]
	[InlineData("removed")]
	[InlineData("left")]
	public async Task NonAcceptedRelationship_ReturnsTypedConflictWithoutMutation(
		string role)
	{
		TestIdentity identity = _scenario[role];
		(EventJoinRequestStatus Status, DateTime? UpdatedUtc) before =
			await ReadRequestStateAsync(identity.RequestId);
		using HttpClient client = _scenario.CreateClient(role);

		using HttpResponseMessage response = await client.PostAsync(
			$"/api/events/{EventScenario.ActiveEventId}/join-requests/mine/leave",
			content: null);
		EventConflictResponse conflict =
			await HttpTestAssertions.ReadConflictAsync(response);

		Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
		Assert.Equal(EventConflictCodes.ParticipantNotAccepted, conflict.Code);
		Assert.Equal("requestId", conflict.Field);
		Assert.Equal(before, await ReadRequestStateAsync(identity.RequestId));
	}

	[Theory]
	[InlineData("stranger", "active")]
	[InlineData("foreign-requester", "active")]
	[InlineData("accepted", "missing")]
	public async Task MissingRelationshipOrEvent_ReturnsCanonicalNotFound(
		string role,
		string target)
	{
		Guid eventId = target == "active"
			? EventScenario.ActiveEventId
			: EventScenario.MissingEventId;
		using HttpClient client = _scenario.CreateClient(role);
		using HttpResponseMessage baselineResponse = await client.PostAsync(
			$"/api/events/{EventScenario.MissingEventId}/join-requests/mine/leave",
			content: null);
		EventConflictResponse baseline =
			await HttpTestAssertions.ReadConflictAsync(baselineResponse);

		using HttpResponseMessage response = await client.PostAsync(
			$"/api/events/{eventId}/join-requests/mine/leave",
			content: null);

		await HttpTestAssertions.AssertCanonicalNotFoundAsync(
			response,
			baseline);
	}
	#endregion

	#region Private methods
	private async Task<(EventJoinRequestStatus Status, DateTime? UpdatedUtc)>
		ReadRequestStateAsync(Guid requestId)
	{
		await using AsyncServiceScope scope =
			fixture.ApiFactory.Services.CreateAsyncScope();
		var dbContext =
			scope.ServiceProvider.GetRequiredService<ChoNaBojoContext>();
		var state = await dbContext.EventJoinRequests
			.AsNoTracking()
			.Where(entity => entity.Id == requestId)
			.Select(entity => new
			{
				entity.Status,
				entity.UpdatedUtc
			})
			.SingleAsync();
		return (state.Status, state.UpdatedUtc);
	}
	#endregion
}
