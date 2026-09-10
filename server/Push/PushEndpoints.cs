using ChoNaBojo.Contracts.DTOs;
using ChoNaBojo.Server.Auth;
using ChoNaBojo.Server.Data;
using ChoNaBojo.Utils.Text;
using ChoNaBojo.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace ChoNaBojo.Server.Push;

public static class PushEndpoints
{
	#region Public methods
	public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapPut("/me/push-installations", RegisterPushInstallationAsync)
			.WithName("MePushInstallationsRegister");

		return endpoints;
	}
	#endregion

	#region Private methods
	private static async Task<IResult> RegisterPushInstallationAsync(
		RegisterPushInstallationRequest request,
		ChoNaBojoContext dbContext,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		ValidationResult validation =
			PushValidation.ValidateRegisterPushInstallationRequest(request);
		if (!validation.IsValid)
		{
			return Results.ValidationProblem(
				validation.Errors.ToDictionary(
					pair => pair.Key,
					pair => pair.Value,
					StringComparer.Ordinal));
		}

		Guid installationId = await UpsertInstallationAsync(
			dbContext,
			httpContext.GetUserId(),
			request.DeviceRegistrationId.Trim(),
			TextNormalization.NormalizeOptionalText(request.AppVersion),
			cancellationToken);

		return Results.Ok(new RegisterPushInstallationResponse(installationId));
	}

	private static async Task<Guid> UpsertInstallationAsync(
		ChoNaBojoContext dbContext,
		Guid userId,
		string deviceRegistrationId,
		string? appVersion,
		CancellationToken cancellationToken)
	{
		var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
		bool shouldCloseConnection = connection.State == System.Data.ConnectionState.Closed;
		if (shouldCloseConnection)
		{
			await connection.OpenAsync(cancellationToken);
		}

		try
		{
			await using var command = new NpgsqlCommand(
				"""
				INSERT INTO "PushInstallations"
					("Id", "UserId", "DeviceRegistrationId", "AppVersion", "CreatedUtc", "LastSeenUtc", "DisabledUtc")
				VALUES
					(gen_random_uuid(), @userId, @deviceRegistrationId, @appVersion, @nowUtc, @nowUtc, NULL)
				ON CONFLICT ("DeviceRegistrationId") DO UPDATE
				SET
					"UserId" = EXCLUDED."UserId",
					"AppVersion" = EXCLUDED."AppVersion",
					"LastSeenUtc" = EXCLUDED."LastSeenUtc",
					"DisabledUtc" = NULL
				RETURNING "Id";
				""",
				connection);
			command.Parameters.AddWithValue("userId", NpgsqlDbType.Uuid, userId);
			command.Parameters.AddWithValue(
				"deviceRegistrationId",
				NpgsqlDbType.Varchar,
				deviceRegistrationId);
			command.Parameters.AddWithValue(
				"appVersion",
				NpgsqlDbType.Varchar,
				(object?)appVersion ?? DBNull.Value);
			command.Parameters.AddWithValue(
				"nowUtc",
				NpgsqlDbType.TimestampTz,
				DateTime.UtcNow);

			object? result = await command.ExecuteScalarAsync(cancellationToken);
			return result is Guid id
				? id
				: throw new InvalidOperationException(
					"Push installation upsert did not return an installation id.");
		}
		finally
		{
			if (shouldCloseConnection)
			{
				await connection.CloseAsync();
			}
		}
	}
	#endregion
}
