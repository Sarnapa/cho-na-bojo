using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using ChoNaBojo.Server.Data.Entities;

namespace ChoNaBojo.Server.Data.Seeding;

/// <summary>
/// Seeds <c>Venues</c> and <c>VenueSports</c> from <c>data/warsaw-venues.csv</c>, which is the
/// source of truth for both. Venues are upserted on the stable CSV id, so edits to the CSV
/// (name, coordinates, address, description) overwrite the stored rows on the next
/// <c>dotnet ef database update</c>. Sport links are reconciled — added when new, dropped when the
/// CSV no longer lists them. Rows already matching the CSV are skipped, keeping repeat runs no-ops.
/// Venues removed from the CSV are deliberately left in place rather than deleted, because
/// <c>Venues</c> is referenced by user-generated data.
/// </summary>
public static class WarsawVenueSeeder
{
	#region Private constants
	private const string DefaultCsvPath = "data/warsaw-venues.csv";
	private const int Wgs84Srid = 4326;
	#endregion
	
	#region Public methods
	public static void Seed(DbContext context, string? configuredCsvPath = null)
	{
		if (context is not ChoNaBojoContext dbContext)
		{
			throw new InvalidOperationException($"Expected {nameof(ChoNaBojoContext)} but got {context.GetType().FullName}.");
		}

		SeedCoreAsync(dbContext, configuredCsvPath, isAsync: false, CancellationToken.None)
			.GetAwaiter()
			.GetResult();
	}

	public static Task SeedAsync(
		DbContext context,
		string? configuredCsvPath = null,
		CancellationToken cancellationToken = default)
	{
		if (context is not ChoNaBojoContext dbContext)
		{
			throw new InvalidOperationException($"Expected {nameof(ChoNaBojoContext)} but got {context.GetType().FullName}.");
		}

		return SeedCoreAsync(dbContext, configuredCsvPath, isAsync: true, cancellationToken);
	}
	#endregion

	#region Private methods
	private static async Task SeedCoreAsync(
		ChoNaBojoContext dbContext,
		string? configuredCsvPath,
		bool isAsync,
		CancellationToken cancellationToken)
	{
		string csvFilePath = ResolveCsvPath(configuredCsvPath);
		var csvRows = LoadCsvRows(csvFilePath);

		var sportIds = isAsync
			? await dbContext.Sports.AsNoTracking().Select(s => s.Id).ToHashSetAsync(cancellationToken)
			: dbContext.Sports.AsNoTracking().Select(s => s.Id).ToHashSet();

		int[] missingSportIds = csvRows
			.SelectMany(row => row.SportIds)
			.Distinct()
			.Where(sportId => !sportIds.Contains(sportId))
			.OrderBy(sportId => sportId)
			.ToArray();

		if (missingSportIds.Length > 0)
		{
			throw new InvalidOperationException(
				$"CSV references sport id(s) with no matching seeded Sport row: {string.Join(", ", missingSportIds)}.");
		}

		var existingVenues = isAsync
			? await dbContext.Venues.AsNoTracking()
				.Select(venue => new VenueSnapshot(
					venue.Id,
					venue.Name,
					venue.Location.Y,
					venue.Location.X,
					venue.Address,
					venue.Description))
				.ToDictionaryAsync(venue => venue.Id, cancellationToken)
			: dbContext.Venues.AsNoTracking()
				.Select(venue => new VenueSnapshot(
					venue.Id,
					venue.Name,
					venue.Location.Y,
					venue.Location.X,
					venue.Address,
					venue.Description))
				.ToDictionary(venue => venue.Id);

		var existingVenueSportPairs = isAsync
			? (await dbContext.VenueSports.AsNoTracking()
				.Select(vs => new VenueSportKey(vs.VenueId, vs.SportId))
				.ToListAsync(cancellationToken)).ToHashSet()
			: dbContext.VenueSports.AsNoTracking()
				.Select(vs => new VenueSportKey(vs.VenueId, vs.SportId))
				.ToHashSet();

		foreach (var row in csvRows)
		{
			// The CSV is the source of truth: insert missing venues and overwrite drifted ones.
			if (existingVenues.TryGetValue(row.Id, out var snapshot) && snapshot.Matches(row))
			{
				continue;
			}

			if (isAsync)
			{
				await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$"""
					INSERT INTO "Venues" ("Id", "Name", "Location", "Address", "Description")
					VALUES ({row.Id}, {row.Name}, ST_SetSRID(ST_MakePoint({row.Longitude}, {row.Latitude}), {Wgs84Srid}), {row.Address}, {row.Description})
					ON CONFLICT ("Id") DO UPDATE SET
						"Name" = EXCLUDED."Name",
						"Location" = EXCLUDED."Location",
						"Address" = EXCLUDED."Address",
						"Description" = EXCLUDED."Description";
					""",
					cancellationToken);
			}
			else
			{
				dbContext.Database.ExecuteSqlInterpolated(
					$"""
					INSERT INTO "Venues" ("Id", "Name", "Location", "Address", "Description")
					VALUES ({row.Id}, {row.Name}, ST_SetSRID(ST_MakePoint({row.Longitude}, {row.Latitude}), {Wgs84Srid}), {row.Address}, {row.Description})
					ON CONFLICT ("Id") DO UPDATE SET
						"Name" = EXCLUDED."Name",
						"Location" = EXCLUDED."Location",
						"Address" = EXCLUDED."Address",
						"Description" = EXCLUDED."Description";
					""");
			}
		}

		var desiredVenueSportPairs = csvRows
			.SelectMany(row => row.SportIds.Select(sportId => new VenueSportKey(row.Id, sportId)))
			.ToHashSet();

		foreach (var pair in desiredVenueSportPairs.Where(pair => !existingVenueSportPairs.Contains(pair)))
		{
			dbContext.VenueSports.Add(new VenueSport
			{
				VenueId = pair.VenueId,
				SportId = pair.SportId
			});
		}

		// Join rows are derived data, so links dropped from the CSV are removed for venues it still owns.
		var csvVenueIds = csvRows.Select(row => row.Id).ToHashSet();
		var staleVenueSportPairs = existingVenueSportPairs
			.Where(pair => csvVenueIds.Contains(pair.VenueId) && !desiredVenueSportPairs.Contains(pair))
			.ToList();

		foreach (var pair in staleVenueSportPairs)
		{
			dbContext.VenueSports.Remove(new VenueSport
			{
				VenueId = pair.VenueId,
				SportId = pair.SportId
			});
		}

		if (!dbContext.ChangeTracker.HasChanges())
		{
			return;
		}

		if (isAsync)
		{
			await dbContext.SaveChangesAsync(cancellationToken);
		}
		else
		{
			dbContext.SaveChanges();
		}
	}

	private static List<VenueCsvRow> LoadCsvRows(string csvFilePath)
	{
		if (!File.Exists(csvFilePath))
		{
			throw new FileNotFoundException("Warsaw venues CSV file was not found.", csvFilePath);
		}

		using var stream = File.OpenRead(csvFilePath);
		using var parser = new TextFieldParser(stream, Encoding.UTF8)
		{
			TextFieldType = FieldType.Delimited,
			HasFieldsEnclosedInQuotes = true,
			TrimWhiteSpace = false
		};

		parser.SetDelimiters(",");

		if (parser.EndOfData)
		{
			throw new InvalidOperationException($"CSV file '{csvFilePath}' is empty.");
		}

		var headerFields = parser.ReadFields()
			?? throw new InvalidOperationException($"CSV file '{csvFilePath}' does not contain a header row.");

		Dictionary<string, int> headerMap = BuildHeaderMap(csvFilePath, headerFields);
		var rows = new List<VenueCsvRow>();
		var seenVenueIds = new HashSet<int>();
		int rowNumber = 1;

		while (!parser.EndOfData)
		{
			rowNumber++;
			string[]? fields = parser.ReadFields();
			if (fields is null || fields.All(string.IsNullOrWhiteSpace))
			{
				continue;
			}

			int id = ParsePositiveInt(GetRequiredValue(fields, headerMap["id"], "id", rowNumber), "id", rowNumber);
			if (!seenVenueIds.Add(id))
			{
				throw new InvalidOperationException($"Duplicate venue id '{id}' found in CSV row {rowNumber}.");
			}

			double latitude = ParseDouble(
				GetRequiredValue(fields, headerMap["latitude"], "latitude", rowNumber),
				"latitude",
				rowNumber);

			double longitude = ParseDouble(
				GetRequiredValue(fields, headerMap["longitude"], "longitude", rowNumber),
				"longitude",
				rowNumber);

			rows.Add(new VenueCsvRow(
				Id: id,
				Name: GetRequiredValue(fields, headerMap["name"], "name", rowNumber),
				Latitude: latitude,
				Longitude: longitude,
				Address: GetRequiredValue(fields, headerMap["address"], "address", rowNumber),
				Description: GetRequiredValue(fields, headerMap["description"], "description", rowNumber),
				SportIds: ParseSportIds(
					GetRequiredValue(fields, headerMap["supported_sports"], "supported_sports", rowNumber),
					rowNumber)));
		}

		return rows;
	}

	private static Dictionary<string, int> BuildHeaderMap(string csvFilePath, string[] rawHeaders)
	{
		var headers = rawHeaders
			.Select((header, index) => new
			{
				Name = header.Trim().TrimStart('\uFEFF'),
				Index = index
			})
			.ToDictionary(entry => entry.Name, entry => entry.Index, StringComparer.OrdinalIgnoreCase);

		string[] requiredHeaders =
		[
			"id",
			"name",
			"latitude",
			"longitude",
			"address",
			"description",
			"supported_sports"
		];

		string[] missingHeaders = requiredHeaders
			.Where(required => !headers.ContainsKey(required))
			.ToArray();

		if (missingHeaders.Length > 0)
		{
			throw new InvalidOperationException(
				$"CSV file '{csvFilePath}' is missing required header(s): {string.Join(", ", missingHeaders)}.");
		}

		return headers;
	}

	private static string GetRequiredValue(string[] fields, int index, string columnName, int rowNumber)
	{
		if (index >= fields.Length)
		{
			throw new InvalidOperationException($"Row {rowNumber} is missing column '{columnName}'.");
		}

		string value = fields[index].Trim();
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException($"Row {rowNumber} contains an empty '{columnName}' value.");
		}

		return value;
	}

	private static int ParsePositiveInt(string value, string columnName, int rowNumber)
	{
		if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue) || parsedValue <= 0)
		{
			throw new InvalidOperationException(
				$"Row {rowNumber} contains invalid '{columnName}' value '{value}'. Expected a positive integer.");
		}

		return parsedValue;
	}

	private static double ParseDouble(string value, string columnName, int rowNumber)
	{
		if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
		{
			throw new InvalidOperationException(
				$"Row {rowNumber} contains invalid '{columnName}' value '{value}'. Expected a decimal number.");
		}

		return parsedValue;
	}

	private static IReadOnlyCollection<int> ParseSportIds(string value, int rowNumber)
	{
		var sportIds = new HashSet<int>();

		foreach (string token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			int sportId = ParsePositiveInt(token, "supported_sports", rowNumber);
			sportIds.Add(sportId);
		}

		if (sportIds.Count == 0)
		{
			throw new InvalidOperationException($"Row {rowNumber} has no sport ids in 'supported_sports'.");
		}

		return sportIds;
	}

	private static string ResolveCsvPath(string? configuredCsvPath)
	{
		string pathToResolve = string.IsNullOrWhiteSpace(configuredCsvPath)
			? DefaultCsvPath
			: configuredCsvPath.Trim();

		if (Path.IsPathRooted(pathToResolve))
		{
			if (!File.Exists(pathToResolve))
			{
				throw new FileNotFoundException("Warsaw venues CSV file was not found.", pathToResolve);
			}

			return pathToResolve;
		}

		var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string startDirectory in EnumerateSearchStartDirectories())
		{
			for (var current = new DirectoryInfo(startDirectory); current is not null; current = current.Parent)
			{
				string candidate = Path.GetFullPath(Path.Combine(current.FullName, pathToResolve));
				if (!checkedPaths.Add(candidate))
				{
					continue;
				}

				if (File.Exists(candidate))
				{
					return candidate;
				}
			}
		}

		throw new FileNotFoundException(
			$"Unable to locate Warsaw venues CSV '{pathToResolve}' from current/content root. " +
			"Set DataSeeding:WarsawVenuesCsvPath to an absolute path or a repo-root-relative path.");
	}

	private static IEnumerable<string> EnumerateSearchStartDirectories()
	{
		yield return Directory.GetCurrentDirectory();
		yield return AppContext.BaseDirectory;
	}
	#endregion

	#region Private types
	private sealed record VenueCsvRow(
		int Id,
		string Name,
		double Latitude,
		double Longitude,
		string Address,
		string Description,
		IReadOnlyCollection<int> SportIds);

	private sealed record VenueSnapshot(
		int Id,
		string Name,
		double Latitude,
		double Longitude,
		string Address,
		string Description)
	{
		/// <summary>Coordinates round-trip through PostGIS as doubles, so compare them within a tolerance.</summary>
		private const double CoordinateTolerance = 1e-7;

		public bool Matches(VenueCsvRow row)
		{
			return string.Equals(Name, row.Name, StringComparison.Ordinal)
				&& string.Equals(Address, row.Address, StringComparison.Ordinal)
				&& string.Equals(Description, row.Description, StringComparison.Ordinal)
				&& Math.Abs(Latitude - row.Latitude) <= CoordinateTolerance
				&& Math.Abs(Longitude - row.Longitude) <= CoordinateTolerance;
		}
	}

	private readonly record struct VenueSportKey(int VenueId, int SportId);
	#endregion
}
