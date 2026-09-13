using ChoNaBojo.Server.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace ChoNaBojo.Server.IntegrationTests.Infrastructure;

public sealed class PostgisFixture : IAsyncLifetime
{
	#region Public constants
	public const string DatabaseName = "chonabojo_integration_tests";
	public const string FixtureApplicationName =
		"ChoNaBojo.Integration.Fixture";
	public const string Image = "postgis/postgis:17-3.5";
	#endregion

	#region Private fields
	private readonly PostgreSqlContainer _container =
		new PostgreSqlBuilder(Image)
			.WithDatabase(DatabaseName)
			.WithUsername("chonabojo_test")
			.WithPassword("chonabojo_test_password")
			.Build();
	private readonly SemaphoreSlim _resetLock = new(1, 1);
	private Respawner? _respawner;
	#endregion

	#region Public properties
	public ChoNaBojoApiFactory ApiFactory { get; private set; } = null!;

	public string ConnectionString { get; private set; } = string.Empty;
	#endregion

	#region Public methods
	public async Task InitializeAsync()
	{
		try
		{
			await _container.StartAsync();
		}
		catch (Exception exception)
		{
			throw new InvalidOperationException(
				$"Integration tests require a reachable Linux Docker engine "
					+ $"capable of running {Image}. No test was skipped and no "
					+ "external database fallback was used.",
				exception);
		}

		ConnectionString = WithApplicationName(
			_container.GetConnectionString(),
			FixtureApplicationName);

		await using (ChoNaBojoContext dbContext = CreateDbContext())
		{
			await dbContext.Database.MigrateAsync();
		}

		await VerifyRequiredExtensionsAsync();

		await using (var connection = new NpgsqlConnection(ConnectionString))
		{
			await connection.OpenAsync();
			_respawner = await Respawner.CreateAsync(
				connection,
				new RespawnerOptions
				{
					DbAdapter = DbAdapter.Postgres,
					SchemasToInclude = ["public"],
					TablesToIgnore =
					[
						new Table("__EFMigrationsHistory"),
						new Table("Sports"),
						new Table("spatial_ref_sys")
					]
				});
		}

		ApiFactory = new ChoNaBojoApiFactory(ConnectionString);
	}

	public async Task ResetAsync()
	{
		if (_respawner is null)
		{
			throw new InvalidOperationException(
				"The PostGIS fixture has not been initialized.");
		}

		await _resetLock.WaitAsync();
		try
		{
			await using var connection = new NpgsqlConnection(ConnectionString);
			await connection.OpenAsync();
			await _respawner.ResetAsync(connection);
		}
		finally
		{
			_resetLock.Release();
		}
	}

	public async Task DisposeAsync()
	{
		ApiFactory?.Dispose();
		_resetLock.Dispose();
		await _container.DisposeAsync();
	}

	private ChoNaBojoContext CreateDbContext()
	{
		var options = new DbContextOptionsBuilder<ChoNaBojoContext>()
			.UseNpgsql(
				ConnectionString,
				npgsql => npgsql.UseNetTopologySuite())
			.Options;
		return new ChoNaBojoContext(options);
	}
	#endregion

	#region Private methods
	private async Task VerifyRequiredExtensionsAsync()
	{
		await using var connection = new NpgsqlConnection(ConnectionString);
		await connection.OpenAsync();
		await using NpgsqlCommand command = connection.CreateCommand();
		command.CommandText =
			"""
			SELECT COUNT(*)
			FROM pg_extension
			WHERE extname IN ('postgis', 'pgcrypto');
			""";

		object? result = await command.ExecuteScalarAsync();
		if (Convert.ToInt32(result) != 2)
		{
			throw new InvalidOperationException(
				"The disposable database is missing postgis or pgcrypto.");
		}
	}

	private static string WithApplicationName(
		string connectionString,
		string applicationName)
	{
		var connectionStringBuilder = new NpgsqlConnectionStringBuilder(
			connectionString)
		{
			ApplicationName = applicationName
		};
		return connectionStringBuilder.ConnectionString;
	}
	#endregion
}
