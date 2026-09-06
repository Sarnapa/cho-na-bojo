namespace ChoNaBojo.Contracts.DTOs;

#region Responses DTOs
/// <summary>
/// Shared shape for the server's RFC-7807 <c>ValidationProblem</c> body — the single agreed
/// contract for every <c>400</c> response, so no feature area declares its own parser.
/// </summary>
public sealed record ValidationProblemResponse(Dictionary<string, string[]> Errors);
#endregion
