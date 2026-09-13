using System.Text.Json;

namespace ChoNaBojo.Server.IntegrationTests.Infrastructure;

internal static class JsonPrivacyAssertions
{
	public static void DoesNotContainKeys(
		string route,
		string callerRole,
		string json,
		params string[] forbiddenKeys)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		HashSet<string> keys = EnumerateKeys(document.RootElement)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		foreach (string forbiddenKey in forbiddenKeys)
		{
			Assert.True(
				!keys.Contains(forbiddenKey),
				$"Route '{route}' for caller '{callerRole}' exposed forbidden "
					+ $"key '{forbiddenKey}'. Response: {json}");
		}
	}

	public static void DoesNotContainMarkers(
		string route,
		string callerRole,
		string body,
		IEnumerable<PrivacyMarker> forbiddenMarkers)
	{
		foreach (PrivacyMarker marker in forbiddenMarkers)
		{
			bool containsMarker = body.Contains(
				marker.Value,
				StringComparison.OrdinalIgnoreCase);
			Assert.True(
				!containsMarker,
				$"Route '{route}' for caller '{callerRole}' exposed forbidden "
					+ $"value '{marker.Name}'. Response: {Redact(body, marker)}");
		}
	}

	public static void ContainsMarker(
		string route,
		string callerRole,
		string body,
		PrivacyMarker marker)
	{
		Assert.True(
			body.Contains(marker.Value, StringComparison.OrdinalIgnoreCase),
			$"Route '{route}' for caller '{callerRole}' omitted permitted "
				+ $"value '{marker.Name}'. Response: {body}");
	}

	private static IEnumerable<string> EnumerateKeys(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty property in element.EnumerateObject())
			{
				yield return property.Name;
				foreach (string nestedKey in EnumerateKeys(property.Value))
				{
					yield return nestedKey;
				}
			}
		}
		else if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in element.EnumerateArray())
			{
				foreach (string nestedKey in EnumerateKeys(item))
				{
					yield return nestedKey;
				}
			}
		}
	}

	private static string Redact(string body, PrivacyMarker marker)
	{
		return body.Replace(
			marker.Value,
			$"<redacted:{marker.Name}>",
			StringComparison.OrdinalIgnoreCase);
	}
}
