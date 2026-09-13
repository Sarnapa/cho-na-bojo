using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using Npgsql;

namespace ChoNaBojo.Server.IntegrationTests.Infrastructure;

internal sealed class PostgresLockCoordinator
{
	#region Public constants
	public const string ControlApplicationName =
		"ChoNaBojo.Integration.Control";
	public const string ObserverApplicationName =
		"ChoNaBojo.Integration.Observer";
	#endregion

	#region Private fields
	private readonly string _controlConnectionString;
	private readonly string _observerConnectionString;
	private readonly TimeSpan _pollInterval;
	private readonly TimeSpan _timeout;
	#endregion

	#region Constructors
	public PostgresLockCoordinator(
		string connectionString,
		TimeSpan? timeout = null,
		TimeSpan? pollInterval = null)
	{
		_controlConnectionString = WithApplicationName(
			connectionString,
			ControlApplicationName);
		_observerConnectionString = WithApplicationName(
			connectionString,
			ObserverApplicationName);
		_timeout = timeout ?? TimeSpan.FromSeconds(10);
		_pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(25);
	}
	#endregion

	#region Public methods
	public async Task<CoordinatedHttpResponses> CoordinateAsync(
		Guid eventId,
		Func<Task<HttpResponseMessage>> firstOperation,
		Func<Task<HttpResponseMessage>> secondOperation,
		CancellationToken cancellationToken = default)
	{
		await using var controlConnection =
			new NpgsqlConnection(_controlConnectionString);
		await controlConnection.OpenAsync(cancellationToken);
		await using NpgsqlTransaction controlTransaction =
			await controlConnection.BeginTransactionAsync(cancellationToken);
		int controlBackendPid = await GetBackendPidAsync(
			controlConnection,
			controlTransaction,
			cancellationToken);
		await LockEventAsync(
			controlConnection,
			controlTransaction,
			eventId,
			cancellationToken);

		Task<HttpResponseMessage> firstTask = firstOperation();
		Task<HttpResponseMessage> secondTask = secondOperation();
		PostgresLockObservation? observation = null;
		TimeoutException? timeoutException = null;
		try
		{
			observation = await WaitForBothApiSessionsAsync(
				controlBackendPid,
				cancellationToken);
		}
		catch (TimeoutException exception)
		{
			timeoutException = exception;
		}
		finally
		{
			await controlTransaction.RollbackAsync(CancellationToken.None);
		}

		HttpResponseMessage[] responses =
			await Task.WhenAll(firstTask, secondTask);
		if (timeoutException is not null)
		{
			foreach (HttpResponseMessage response in responses)
			{
				response.Dispose();
			}

			ExceptionDispatchInfo.Capture(timeoutException).Throw();
		}

		return new CoordinatedHttpResponses(
			responses[0],
			responses[1],
			observation
				?? throw new InvalidOperationException(
					"Lock observation completed without a result."));
	}
	#endregion

	#region Private methods
	private async Task<PostgresLockObservation> WaitForBothApiSessionsAsync(
		int controlBackendPid,
		CancellationToken cancellationToken)
	{
		await using var observerConnection =
			new NpgsqlConnection(_observerConnectionString);
		await observerConnection.OpenAsync(cancellationToken);
		var stopwatch = Stopwatch.StartNew();
		IReadOnlyList<PostgresSessionSnapshot> lastSessions = [];

		while (true)
		{
			lastSessions = await ReadApiSessionsAsync(
				observerConnection,
				cancellationToken);
			PostgresSessionSnapshot[] waitingSessions = lastSessions
				.Where(session =>
					string.Equals(
						session.WaitEventType,
						"Lock",
						StringComparison.Ordinal)
					&& session.Query.Contains(
						"SportsEvents",
						StringComparison.Ordinal)
					&& session.Query.Contains(
						"FOR UPDATE",
						StringComparison.OrdinalIgnoreCase))
				.ToArray();
			if (waitingSessions.Length >= 2)
			{
				return new PostgresLockObservation(
					controlBackendPid,
					waitingSessions);
			}

			if (stopwatch.Elapsed >= _timeout)
			{
				throw new TimeoutException(
					BuildTimeoutDiagnostic(controlBackendPid, lastSessions));
			}

			await Task.Delay(_pollInterval, cancellationToken);
		}
	}

	private static async Task<IReadOnlyList<PostgresSessionSnapshot>>
		ReadApiSessionsAsync(
			NpgsqlConnection observerConnection,
			CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = observerConnection.CreateCommand();
		command.CommandText =
			"""
			SELECT
				pid,
				state,
				wait_event_type,
				wait_event,
				query,
				pg_blocking_pids(pid)
			FROM pg_stat_activity
			WHERE datname = current_database()
				AND application_name = @applicationName
				AND pid <> pg_backend_pid()
			ORDER BY pid;
			""";
		command.Parameters.AddWithValue(
			"applicationName",
			ChoNaBojoApiFactory.ApiApplicationName);

		var sessions = new List<PostgresSessionSnapshot>();
		await using NpgsqlDataReader reader =
			await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			sessions.Add(new PostgresSessionSnapshot(
				reader.GetInt32(0),
				reader.GetString(1),
				reader.IsDBNull(2) ? null : reader.GetString(2),
				reader.IsDBNull(3) ? null : reader.GetString(3),
				reader.GetString(4),
				reader.GetFieldValue<int[]>(5)));
		}

		return sessions;
	}

	private static async Task<int> GetBackendPidAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = "SELECT pg_backend_pid();";
		object? result = await command.ExecuteScalarAsync(cancellationToken);
		return Convert.ToInt32(result);
	}

	private static async Task LockEventAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		Guid eventId,
		CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText =
			"""
			SELECT 1
			FROM "SportsEvents"
			WHERE "Id" = @eventId
			FOR UPDATE;
			""";
		command.Parameters.AddWithValue("eventId", eventId);
		object? result = await command.ExecuteScalarAsync(cancellationToken);
		if (result is null)
		{
			throw new InvalidOperationException(
				$"Cannot coordinate a lock for missing event {eventId}.");
		}
	}

	private static string BuildTimeoutDiagnostic(
		int controlBackendPid,
		IReadOnlyList<PostgresSessionSnapshot> sessions)
	{
		var diagnostic = new StringBuilder()
			.Append(
				"Timed out waiting for two API sessions to block on the "
					+ "SportsEvents FOR UPDATE query. ")
			.Append("Control PID: ")
			.Append(controlBackendPid)
			.AppendLine(".")
			.AppendLine(
				"Last API session diagnostics (PID/state/wait/query/blockers):");

		if (sessions.Count == 0)
		{
			diagnostic.Append("<none>");
		}
		else
		{
			foreach (PostgresSessionSnapshot session in sessions)
			{
				diagnostic.AppendLine(session.ToDiagnosticString());
			}
		}

		return diagnostic.ToString();
	}

	private static string WithApplicationName(
		string connectionString,
		string applicationName)
	{
		var builder = new NpgsqlConnectionStringBuilder(connectionString)
		{
			ApplicationName = applicationName
		};
		return builder.ConnectionString;
	}
	#endregion
}

internal sealed class CoordinatedHttpResponses(
	HttpResponseMessage first,
	HttpResponseMessage second,
	PostgresLockObservation observation) : IDisposable
{
	public HttpResponseMessage First { get; } = first;

	public HttpResponseMessage Second { get; } = second;

	public PostgresLockObservation Observation { get; } = observation;

	public IReadOnlyList<HttpResponseMessage> All => [First, Second];

	public void Dispose()
	{
		First.Dispose();
		Second.Dispose();
	}
}

internal sealed record PostgresLockObservation(
	int ControlBackendPid,
	IReadOnlyList<PostgresSessionSnapshot> WaitingSessions)
{
	public string ToDiagnosticString()
	{
		var diagnostic = new StringBuilder()
			.Append("Control PID: ")
			.Append(ControlBackendPid)
			.AppendLine();
		foreach (PostgresSessionSnapshot session in WaitingSessions)
		{
			diagnostic.AppendLine(session.ToDiagnosticString());
		}

		return diagnostic.ToString();
	}
}

internal sealed record PostgresSessionSnapshot(
	int ProcessId,
	string State,
	string? WaitEventType,
	string? WaitEvent,
	string Query,
	IReadOnlyList<int> BlockingProcessIds)
{
	public string ToDiagnosticString()
	{
		string normalizedQuery = Query
			.Replace("\r", " ", StringComparison.Ordinal)
			.Replace("\n", " ", StringComparison.Ordinal)
			.Trim();
		return $"PID={ProcessId}; state={State}; "
			+ $"wait={WaitEventType ?? "<none>"}/{WaitEvent ?? "<none>"}; "
			+ $"blockers=[{string.Join(",", BlockingProcessIds)}]; "
			+ $"query={normalizedQuery}";
	}
}
