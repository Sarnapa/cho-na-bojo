using System.Net;
using ChoNaBojo.Server.Data;
using ChoNaBojo.Server.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChoNaBojo.Server.IntegrationTests.Events;

[Collection(IntegrationTestCollection.Name)]
public sealed class UnauthorizedEventMutationTests(PostgisFixture fixture) :
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
	[MemberData(nameof(ForbiddenMutationCases))]
	public async Task ForbiddenMutation_IsCloakedAndChangesNothing(
		string operation,
		string actorRole,
		string eventTarget,
		string requestTarget)
	{
		Guid eventId = ResolveEventId(eventTarget);
		Guid requestId = ResolveRequestId(requestTarget);
		using HttpClient client = _scenario.CreateClient(actorRole);

		using HttpResponseMessage baselineResponse = await client.PostAsync(
			BuildRoute(
				operation,
				EventScenario.MissingEventId,
				EventScenario.MissingRequestId),
			content: null);
		var baseline =
			await HttpTestAssertions.ReadConflictAsync(baselineResponse);
		MutationSnapshot before = await CaptureStateAsync();

		using HttpResponseMessage response = await client.PostAsync(
			BuildRoute(operation, eventId, requestId),
			content: null);

		await HttpTestAssertions.AssertCanonicalNotFoundAsync(
			response,
			baseline);
		MutationSnapshot after = await CaptureStateAsync();
		Assert.Equal(before, after);
	}
	#endregion

	#region Public methods
	public static IEnumerable<object[]> ForbiddenMutationCases()
	{
		foreach (string operation in new[] { "accept", "reject", "remove" })
		{
			yield return [operation, "accepted", "active", "pending"];
			yield return [operation, "organizer", "foreign", "pending"];
			yield return [operation, "organizer", "active", "fabricated"];
			yield return [operation, "organizer", "missing", "fabricated"];
		}

		yield return ["cancel", "accepted", "active", "unused"];
		yield return ["cancel", "organizer", "foreign", "unused"];
		yield return ["cancel", "organizer", "missing", "unused"];
	}
	#endregion

	#region Private methods
	private async Task<MutationSnapshot> CaptureStateAsync()
	{
		EventRow[] events;
		RequestRow[] requests;
		await using (AsyncServiceScope scope =
			fixture.ApiFactory.Services.CreateAsyncScope())
		{
			var dbContext =
				scope.ServiceProvider.GetRequiredService<ChoNaBojoContext>();
			events = await dbContext.SportsEvents
				.AsNoTracking()
				.OrderBy(entity => entity.Id)
				.Select(entity => new EventRow(
					entity.Id,
					entity.Status,
					entity.StatusChangedUtc,
					1 + entity.EventJoinRequests.Count(request =>
						request.Status
							== Contracts.Enums.EventJoinRequestStatus.Accepted)))
				.ToArrayAsync();
			requests = await dbContext.EventJoinRequests
				.AsNoTracking()
				.OrderBy(entity => entity.Id)
				.Select(entity => new RequestRow(
					entity.Id,
					entity.SportsEventId,
					entity.Status,
					entity.UpdatedUtc))
				.ToArrayAsync();
		}

		using HttpClient organizer = _scenario.CreateClient("organizer");
		using HttpClient foreignOrganizer =
			_scenario.CreateClient("foreign-organizer");
		string mainQueue = await organizer.GetStringAsync(
			$"/api/events/{EventScenario.ActiveEventId}/join-requests");
		string mainContacts = await organizer.GetStringAsync(
			$"/api/events/{EventScenario.ActiveEventId}/contacts");
		string foreignQueue = await foreignOrganizer.GetStringAsync(
			$"/api/events/{EventScenario.ForeignEventId}/join-requests");
		string foreignContacts = await foreignOrganizer.GetStringAsync(
			$"/api/events/{EventScenario.ForeignEventId}/contacts");

		return new MutationSnapshot(
			string.Join("|", events.Select(row => row.ToString())),
			string.Join("|", requests.Select(row => row.ToString())),
			mainQueue,
			mainContacts,
			foreignQueue,
			foreignContacts);
	}

	private Guid ResolveEventId(string target)
	{
		return target switch
		{
			"active" => EventScenario.ActiveEventId,
			"foreign" => EventScenario.ForeignEventId,
			"missing" => EventScenario.MissingEventId,
			_ => throw new ArgumentOutOfRangeException(nameof(target))
		};
	}

	private Guid ResolveRequestId(string target)
	{
		return target switch
		{
			"pending" => _scenario["pending"].RequestId,
			"foreign" => _scenario["foreign-requester"].RequestId,
			"fabricated" => EventScenario.MissingRequestId,
			"unused" => Guid.Empty,
			_ => throw new ArgumentOutOfRangeException(nameof(target))
		};
	}

	private static string BuildRoute(
		string operation,
		Guid eventId,
		Guid requestId)
	{
		return operation == "cancel"
			? $"/api/events/{eventId}/cancel"
			: $"/api/events/{eventId}/join-requests/{requestId}/{operation}";
	}
	#endregion

	#region Private records
	private sealed record MutationSnapshot(
		string Events,
		string Requests,
		string MainQueue,
		string MainContacts,
		string ForeignQueue,
		string ForeignContacts);

	private sealed record EventRow(
		Guid Id,
		Contracts.Enums.EventStatus Status,
		DateTime? StatusChangedUtc,
		int ParticipantCount);

	private sealed record RequestRow(
		Guid Id,
		Guid EventId,
		Contracts.Enums.EventJoinRequestStatus Status,
		DateTime? UpdatedUtc);
	#endregion
}
