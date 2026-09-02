using Android.Locations;
using ChoNaBojo.App.Services.Geocoding;
using MauiLocation = Microsoft.Maui.Devices.Sensors.Location;

namespace ChoNaBojo.App.Platforms.Android;

public sealed class AndroidAddressSearchService : IAddressSearchService
{
	#region Private static fields
	private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(8);
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

		// IsPresent only reports that a provider is installed; individual lookups can still fail.
		if (!Geocoder.IsPresent)
		{
			return AddressSearchResult.Unavailable();
		}

		try
		{
			IList<Address> addresses = await GetAddressesAsync(query.Trim())
				.WaitAsync(SearchTimeout, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();

			var displayNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			IReadOnlyList<AddressSuggestion> suggestions = addresses
				.Take(5)
				.Select(ToSuggestion)
				.Where(suggestion => displayNames.Add(suggestion.DisplayName))
				.ToList();

			return suggestions.Count == 0
				? AddressSearchResult.NotFound()
				: AddressSearchResult.Success(suggestions);
		}
		catch (TimeoutException)
		{
			return AddressSearchResult.Unavailable();
		}
		// Stale emulator providers can surface transport failures such as "grpc failed".
		catch (Java.IO.IOException)
		{
			return AddressSearchResult.Unavailable();
		}
		catch (System.IO.IOException)
		{
			return AddressSearchResult.Unavailable();
		}
		catch (Java.Lang.RuntimeException exception)
			when (IsGeocoderTransportFailure(exception))
		{
			return AddressSearchResult.Unavailable();
		}
	}
	#endregion

	#region Private static methods
	private static Task<IList<Address>> GetAddressesAsync(string query)
	{
		if (OperatingSystem.IsAndroidVersionAtLeast(33))
		{
			var completion =
				new TaskCompletionSource<IList<Address>>(
					TaskCreationOptions.RunContinuationsAsynchronously);
			var geocoder = new Geocoder(
				global::Android.App.Application.Context,
				Java.Util.Locale.Default!);
			var listener = new GeocodeListener(completion);
			try
			{
				geocoder.GetFromLocationName(query, 5, listener);
			}
			catch
			{
				listener.Dispose();
				geocoder.Dispose();
				throw;
			}

			_ = completion.Task.ContinueWith(
				_ =>
				{
					listener.Dispose();
					geocoder.Dispose();
				},
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);

			return completion.Task;
		}

		return Task.Run(() =>
		{
			using var geocoder = new Geocoder(
				global::Android.App.Application.Context,
				Java.Util.Locale.Default!);
#pragma warning disable CA1422
			return geocoder.GetFromLocationName(query, 5) ?? [];
#pragma warning restore CA1422
		});
	}

	private static AddressSuggestion ToSuggestion(Address address)
	{
		var location = new MauiLocation(address.Latitude, address.Longitude);
		string fallbackName = $"{address.Latitude:F5}, {address.Longitude:F5}";
		string displayName = address.MaxAddressLineIndex >= 0
			? address.GetAddressLine(0) ?? fallbackName
			: FormatAddress(address);

		if (string.IsNullOrWhiteSpace(displayName))
		{
			displayName = fallbackName;
		}

		return new AddressSuggestion(displayName, location);
	}

	private static string FormatAddress(Address address)
	{
		string?[] parts =
		[
			address.FeatureName,
			address.Thoroughfare,
			address.Locality,
			address.AdminArea,
			address.CountryName
		];

		return string.Join(
			", ",
			parts
				.Where(part => !string.IsNullOrWhiteSpace(part))
				.Distinct(StringComparer.OrdinalIgnoreCase));
	}

	private static bool IsGeocoderTransportFailure(Exception exception)
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
	#endregion

	#region GeocodeListener
	private sealed class GeocodeListener(
		TaskCompletionSource<IList<Address>> completion)
		: Java.Lang.Object, Geocoder.IGeocodeListener
	{
		public void OnError(string? errorMessage)
		{
			completion.TrySetException(
				new System.IO.IOException(
					string.IsNullOrWhiteSpace(errorMessage)
						? "The Android geocoder request failed."
						: errorMessage));
		}

		public void OnGeocode(IList<Address> addresses)
		{
			completion.TrySetResult(addresses);
		}
	}
	#endregion
}
