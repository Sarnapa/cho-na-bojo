using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using NetTopologySuite.Geometries;
using ChoNaBojo.Server.Data.Entities;

namespace ChoNaBojo.Server.Data.Seeding;

public static class WarsawVenueSeeder
{
	private const string DefaultCsvPath = "data/warsaw-venues.csv";
	private const int Wgs84Srid = 4326;

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

	private static async Task SeedCoreAsync(
		ChoNaBojoContext dbContext,
		string? configuredCsvPath,
		bool isAsync,
		CancellationToken cancellationToken)
	{
		var csvFilePath = ResolveCsvPath(configuredCsvPath);
		var csvRows = LoadCsvRows(csvFilePath);

		var sportIds = isAsync
			? await dbContext.Sports.AsNoTracking().Select(s => s.Id).ToHashSetAsync(cancellationToken)
			: dbContext.Sports.AsNoTracking().Select(s => s.Id).ToHashSet();

		var missingSportIds = csvRows
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

		var existingVenueIds = isAsync
			? await dbContext.Venues.AsNoTracking().Select(venue => venue.Id).ToHashSetAsync(cancellationToken)
			: dbContext.Venues.AsNoTracking().Select(venue => venue.Id).ToHashSet();

		var existingVenueSportPairs = isAsync
			? (await dbContext.VenueSports.AsNoTracking()
				.Select(vs => new VenueSportKey(vs.VenueId, vs.SportId))
				.ToListAsync(cancellationToken)).ToHashSet()
			: dbContext.VenueSports.AsNoTracking()
				.Select(vs => new VenueSportKey(vs.VenueId, vs.SportId))
				.ToHashSet();

		foreach (var row in csvRows)
		{
			if (!existingVenueIds.Contains(row.Id))
			{
				dbContext.Venues.Add(new Venue
				{
					Id = row.Id,
					Name = row.Name,
					Address = row.Address,
					Description = row.Description,
					Location = new Point(row.Longitude, row.Latitude) { SRID = Wgs84Srid }
				});

				existingVenueIds.Add(row.Id);
			}

			foreach (var sportId in row.SportIds)
			{
				var key = new VenueSportKey(row.Id, sportId);
				if (existingVenueSportPairs.Add(key))
				{
					dbContext.VenueSports.Add(new VenueSport
					{
						VenueId = row.Id,
						SportId = sportId
					});
				}
			}
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

		var headerMap = BuildHeaderMap(csvFilePath, headerFields);
		var rows = new List<VenueCsvRow>();
		var seenVenueIds = new HashSet<int>();
		var rowNumber = 1;

		while (!parser.EndOfData)
		{
			rowNumber++;
			var fields = parser.ReadFields();
			if (fields is null || fields.All(string.IsNullOrWhiteSpace))
			{
				continue;
			}

			var id = ParsePositiveInt(GetRequiredValue(fields, headerMap["id"], "id", rowNumber), "id", rowNumber);
			if (!seenVenueIds.Add(id))
			{
				throw new InvalidOperationException($"Duplicate venue id '{id}' found in CSV row {rowNumber}.");
			}

			var latitude = ParseDouble(
				GetRequiredValue(fields, headerMap["szerokosc_geograficzna"], "szerokosc_geograficzna", rowNumber),
				"szerokosc_geograficzna",
				rowNumber);

			var longitude = ParseDouble(
				GetRequiredValue(fields, headerMap["dlugosc_geograficzna"], "dlugosc_geograficzna", rowNumber),
				"dlugosc_geograficzna",
				rowNumber);

			rows.Add(new VenueCsvRow(
				Id: id,
				Name: GetRequiredValue(fields, headerMap["nazwa"], "nazwa", rowNumber),
				Latitude: latitude,
				Longitude: longitude,
				Address: GetRequiredValue(fields, headerMap["adres"], "adres", rowNumber),
				Description: GetRequiredValue(fields, headerMap["opis"], "opis", rowNumber),
				SportIds: ParseSportIds(
					GetRequiredValue(fields, headerMap["wspierane_dyscypliny"], "wspierane_dyscypliny", rowNumber),
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
			"nazwa",
			"szerokosc_geograficzna",
			"dlugosc_geograficzna",
			"adres",
			"opis",
			"wspierane_dyscypliny"
		];

		var missingHeaders = requiredHeaders
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

		var value = fields[index].Trim();
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

		foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var sportId = ParsePositiveInt(token, "wspierane_dyscypliny", rowNumber);
			sportIds.Add(sportId);
		}

		if (sportIds.Count == 0)
		{
			throw new InvalidOperationException($"Row {rowNumber} has no sport ids in 'wspierane_dyscypliny'.");
		}

		return sportIds;
	}

	private static string ResolveCsvPath(string? configuredCsvPath)
	{
		var pathToResolve = string.IsNullOrWhiteSpace(configuredCsvPath)
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
		foreach (var startDirectory in EnumerateSearchStartDirectories())
		{
			for (var current = new DirectoryInfo(startDirectory); current is not null; current = current.Parent)
			{
				var candidate = Path.GetFullPath(Path.Combine(current.FullName, pathToResolve));
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

	private sealed record VenueCsvRow(
		int Id,
		string Name,
		double Latitude,
		double Longitude,
		string Address,
		string Description,
		IReadOnlyCollection<int> SportIds);

	private readonly record struct VenueSportKey(int VenueId, int SportId);
}
