namespace ChoNaBojo.Server.Data.Entities;

/// <summary>
/// A predefined sport discipline. Referenced by stable <see cref="Id"/> / <see cref="Code"/>,
/// never by <see cref="Name"/>. The 10 seeded rows are a frozen contract.
/// </summary>
public class Sport
{
	public int Id
	{
		get; set;
	}

	/// <summary>Stable identifier (e.g. "football"). Required and unique.</summary>
	public string Code { get; set; } = null!;

	/// <summary>Display name (e.g. "Piłka nożna").</summary>
	public string Name { get; set; } = null!;

	public ICollection<VenueSport> VenueSports { get; set; } = [];
}
