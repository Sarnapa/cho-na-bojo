using Microsoft.Maui.Devices.Sensors;

namespace ChoNaBojo.App.Services.Geocoding;

public sealed class AddressSearchService : IAddressSearchService
{
	#region Private fields
	private readonly SemaphoreSlim _searchGate = new(1, 1);
	#endregion

	#region Public methods
	public async Task<AddressSearchResult> SearchAsync(
		string query,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			return AddressSearchResult.NotFound();
		}

#if ANDROID
		if (!Android.Locations.Geocoder.IsPresent)
		{
			return AddressSearchResult.Unavailable();
		}
#endif

		await _searchGate.WaitAsync(cancellationToken);
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			IReadOnlyList<Location> locations =
				(await Microsoft.Maui.Devices.Sensors.Geocoding.Default
					.GetLocationsAsync(query.Trim()))
				.Take(5)
				.ToList();
			cancellationToken.ThrowIfCancellationRequested();

			if (locations.Count == 0)
			{
				// Empty results are expected on emulators without a working geocoder backend.
				return AddressSearchResult.NotFound();
			}

			var suggestions = new List<AddressSuggestion>();
			var displayNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (Location location in locations)
			{
				cancellationToken.ThrowIfCancellationRequested();
				IEnumerable<Placemark> placemarks =
					await Microsoft.Maui.Devices.Sensors.Geocoding.Default
						.GetPlacemarksAsync(location);
				cancellationToken.ThrowIfCancellationRequested();

				Placemark? placemark = placemarks.FirstOrDefault();
				string fallbackName = $"{location.Latitude:F5}, {location.Longitude:F5}";
				string displayName = placemark is null ? fallbackName : FormatAddress(placemark);
				if (string.IsNullOrWhiteSpace(displayName))
				{
					displayName = fallbackName;
				}

				if (displayNames.Add(displayName))
				{
					suggestions.Add(new AddressSuggestion(displayName, location));
				}
			}

			return suggestions.Count == 0
				? AddressSearchResult.NotFound()
				: AddressSearchResult.Success(suggestions);
		}
		catch (System.IO.IOException)
		{
			return AddressSearchResult.Unavailable();
		}
		catch (FeatureNotSupportedException)
		{
			return AddressSearchResult.Unavailable();
		}
		catch (PermissionException)
		{
			return AddressSearchResult.Unavailable();
		}
#if ANDROID
		catch (Java.IO.IOException)
		{
			return AddressSearchResult.Unavailable();
		}
		catch (Java.Lang.RuntimeException exception)
			when (IsAndroidGeocoderTransportFailure(exception))
		{
			return AddressSearchResult.Unavailable();
		}
#endif
		finally
		{
			_searchGate.Release();
		}
	}
	#endregion

	#region Private static methods
	private static string FormatAddress(Placemark placemark)
	{
		string[] parts =
		[
			placemark.FeatureName,
			placemark.Thoroughfare,
			placemark.Locality,
			placemark.AdminArea,
			placemark.CountryName
		];

		return string.Join(
			", ",
			parts
				.Where(part => !string.IsNullOrWhiteSpace(part))
				.Distinct(StringComparer.OrdinalIgnoreCase));
	}

#if ANDROID
	private static bool IsAndroidGeocoderTransportFailure(Exception exception)
	{
		var pending = new Stack<Exception>();
		var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
		pending.Push(exception);

		while (pending.TryPop(out Exception? current))
		{
			if (!visited.Add(current))
			{
				continue;
			}

			if (current is Java.IO.IOException)
			{
				return true;
			}

			if (current.InnerException is not null)
			{
				pending.Push(current.InnerException);
			}

			if (current is Java.Lang.Throwable { Cause: { } cause }
				&& !ReferenceEquals(cause, current))
			{
				pending.Push(cause);
			}
		}

		return false;
	}
#endif
	#endregion
}
