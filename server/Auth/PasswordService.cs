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
}

/// <summary>
/// Wraps <see cref="PasswordHasher{TUser}"/> to centralize hashing and verification behavior.
/// </summary>
public class PasswordService: IPasswordService
{
	private readonly PasswordHasher<User> _passwordHasher = new();

	public string Hash(User user, string password)
	{
		return _passwordHasher.HashPassword(user, password);
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
