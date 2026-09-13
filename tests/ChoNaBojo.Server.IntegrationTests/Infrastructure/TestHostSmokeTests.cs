using System.Net;
using System.Net.Http.Headers;
using ChoNaBojo.Server.Data;
using ChoNaBojo.Server.Events;
using ChoNaBojo.Server.Push;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace ChoNaBojo.Server.IntegrationTests.Infrastructure;

[Collection(IntegrationTestCollection.Name)]
public sealed class TestHostSmokeTests(PostgisFixture fixture) :
	IAsyncLifetime
{
	#region Public methods
	public Task InitializeAsync()
	{
		return fixture.ResetAsync();
	}

	public Task DisposeAsync()
	{
		return Task.CompletedTask;
	}
	#endregion

	#region Test methods
	[Fact]
	public async Task Health_RespondsThroughProductionPipeline()
	{
		using HttpClient client = fixture.ApiFactory.CreateHttpsClient();

		using HttpResponseMessage response = await client.GetAsync("/health");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains(
			"healthy",
			await response.Content.ReadAsStringAsync(),
			StringComparison.Ordinal);
	}

	[Fact]
	public async Task Database_UsesIsolatedNameApplicationAndMigrations()
	{
		await using AsyncServiceScope scope =
			fixture.ApiFactory.Services.CreateAsyncScope();
		var dbContext =
			scope.ServiceProvider.GetRequiredService<ChoNaBojoContext>();
		await dbContext.Database.OpenConnectionAsync();
		try
		{
			var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
			Assert.Equal(PostgisFixture.DatabaseName, connection.Database);
			Assert.Contains(
				"integration",
				connection.Database,
				StringComparison.OrdinalIgnoreCase);

			await using NpgsqlCommand applicationNameCommand =
				connection.CreateCommand();
			applicationNameCommand.CommandText = "SHOW application_name;";
			Assert.Equal(
				ChoNaBojoApiFactory.ApiApplicationName,
				Convert.ToString(
					await applicationNameCommand.ExecuteScalarAsync()));

			Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
			Assert.NotEmpty(await dbContext.Database.GetAppliedMigrationsAsync());

			await using NpgsqlCommand extensionCommand =
				connection.CreateCommand();
			extensionCommand.CommandText =
				"""
				SELECT extname
				FROM pg_extension
				WHERE extname IN ('postgis', 'pgcrypto')
				ORDER BY extname;
				""";
			await using NpgsqlDataReader reader =
				await extensionCommand.ExecuteReaderAsync();
			var extensions = new List<string>();
			while (await reader.ReadAsync())
			{
				extensions.Add(reader.GetString(0));
			}

			Assert.Equal(["pgcrypto", "postgis"], extensions);
		}
		finally
		{
			await dbContext.Database.CloseConnectionAsync();
		}
	}

	[Fact]
	public void Host_DoesNotRegisterProductionWorkers()
	{
		IHostedService[] hostedServices = fixture.ApiFactory.Services
			.GetServices<IHostedService>()
			.ToArray();

		Assert.DoesNotContain(
			hostedServices,
			service => service is PushDeliveryWorker);
		Assert.DoesNotContain(
			hostedServices,
			service => service is EventAutoCloseWorker);
	}

	[Theory]
	[MemberData(nameof(MalformedIdentityTokens))]
	public async Task ProtectedRoute_RejectsMalformedStableIdentity(string token)
	{
		using HttpClient client = fixture.ApiFactory.CreateHttpsClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", token);

		using HttpResponseMessage response =
			await client.GetAsync("/api/me/events");

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task ProtectedRoute_RejectsMissingBearerToken()
	{
		using HttpClient client = fixture.ApiFactory.CreateHttpsClient();

		using HttpResponseMessage response =
			await client.GetAsync("/api/me/events");

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}
	#endregion

	#region Public properties
	public static TheoryData<string> MalformedIdentityTokens
	{
		get
		{
			return new()
			{
				TestJwtFactory.CreateTokenWithoutStableIdentity(),
				TestJwtFactory.CreateTokenWithInvalidStableIdentity(),
				TestJwtFactory.CreateTokenWithEmptyStableIdentity()
			};
		}
	}
	#endregion
}
