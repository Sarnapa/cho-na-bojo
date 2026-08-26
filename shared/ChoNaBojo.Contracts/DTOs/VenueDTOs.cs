namespace ChoNaBojo.Contracts.DTOs;

#region Responses DTOs
public sealed record VenueResponse(
	int Id,
	string Name,
	string Address,
	string Description,
	double Latitude,
	double Longitude,
	IReadOnlyList<int> SportIds);

public sealed record SportResponse(int Id, string Code, string Name);
#endregion
