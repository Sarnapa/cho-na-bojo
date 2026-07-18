using Microsoft.AspNetCore.Identity;
using ChoNaBojo.Server.Data.Entities;

namespace ChoNaBojo.Server.Auth;

public enum PasswordVerificationOutcome
{
	Failed = 0,
	Success = 1,
	SuccessRehashNeeded = 2
}

public interface IPasswordService
{
	string Hash(User user, string password);
	PasswordVerificationOutcome Verify(User user, string password);
	void PerformDummyVerification(string password);
}

/// <summary>
/// Wraps <see cref="PasswordHasher{TUser}"/> to centralize hashing and verification behavior.
/// </summary>
public class PasswordService: IPasswordService
{
	private readonly PasswordHasher<User> _passwordHasher = new();

	private static readonly User DummyUser = new();
	private static readonly string DummyPasswordHash = new PasswordHasher<User>()
		.HashPassword(DummyUser, "timing-attack-mitigation-placeholder");

	public string Hash(User user, string password)
	{
		return _passwordHasher.HashPassword(user, password);
	}

	/// <summary>
	/// Runs a verification against a constant fake hash so that authentication attempts for
	/// non-existent users take comparable time to real ones, removing a user-enumeration timing oracle.
	/// </summary>
	public void PerformDummyVerification(string password)
	{
		_ = _passwordHasher.VerifyHashedPassword(DummyUser, DummyPasswordHash, password);
	}

	public PasswordVerificationOutcome Verify(User user, string password)
	{
		var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);

		return verificationResult switch
		{
			PasswordVerificationResult.Success => PasswordVerificationOutcome.Success,
			PasswordVerificationResult.SuccessRehashNeeded => PasswordVerificationOutcome.SuccessRehashNeeded,
			_ => PasswordVerificationOutcome.Failed
		};
	}
}
