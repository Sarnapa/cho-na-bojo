namespace ChoNaBojo.Validation;

/// <summary>
/// Framework-neutral validation outcome. Consumers map <see cref="Errors"/> onto their
/// own transport — the API to <c>Results.ValidationProblem</c>, the MAUI client to inline
/// field errors — so a single rule set drives both without drift.
/// </summary>
public sealed class ValidationResult
{
	#region Private fields
	private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
		new Dictionary<string, string[]>(StringComparer.Ordinal);
	#endregion

	#region Public static properties
	public static ValidationResult Valid { get; } = new(EmptyErrors);
	#endregion

	#region Public properties
	public IReadOnlyDictionary<string, string[]> Errors
	{
		get;
	}

	public bool IsValid
	{
		get
		{
			return Errors.Count == 0;
		}
	}
	#endregion

	#region Constructors
	public ValidationResult(IReadOnlyDictionary<string, string[]> errors)
	{
		Errors = errors;
	}
	#endregion
}
