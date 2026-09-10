using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ChoNaBojo.Server.Data;
using ChoNaBojo.Server.Data.Entities;

namespace ChoNaBojo.Server.Auth;

#region Public types
public sealed record TokenPair(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresUtc);

public enum RefreshTokenExchangeFailure
{
	Invalid = 0,
	ReuseDetected = 1,
	RetryInProgress = 2
}

public sealed record RefreshTokenExchangeResult(TokenPair? Pair, RefreshTokenExchangeFailure? Failure)
{
	public bool Succeeded
	{
		get
		{
			return Pair is not null;
		}
	}

	public static RefreshTokenExchangeResult Success(TokenPair pair)
	{
		return new(pair, null);
	}

	public static RefreshTokenExchangeResult Failed(RefreshTokenExchangeFailure failure)
	{
		return new(null, failure);
	}
}
#endregion

#region IRefreshTokenService interface
public interface IRefreshTokenService
{
	Task<TokenPair> IssueForLoginAsync(User user, string? createdByIp, CancellationToken cancellationToken);
	Task<RefreshTokenExchangeResult> RotateAsync(string refreshToken, string? createdByIp, CancellationToken cancellationToken);
	Task<Guid?> RevokeFamilyAsync(string refreshToken, CancellationToken cancellationToken);
}
#endregion

#region RefreshTokenService implementation
/// <summary>
/// Issues, rotates, and revokes hashed refresh tokens with family-based reuse detection.
/// </summary>
public class RefreshTokenService(
	ChoNaBojoContext dbContext,
	ITokenService tokenService,
	IOptions<JwtOptions> jwtOptionsAccessor): IRefreshTokenService
{
	#region Private constants
	private const int RefreshTokenByteLength = 64;
	private static readonly TimeSpan RetryGraceWindow = TimeSpan.FromSeconds(20);
	#endregion

	#region Private fields
	private readonly ChoNaBojoContext _dbContext = dbContext;
	private readonly ITokenService _tokenService = tokenService;
	private readonly JwtOptions _jwtOptions = jwtOptionsAccessor.Value;
	#endregion

	#region Public methods
	public async Task<TokenPair> IssueForLoginAsync(User user, string? createdByIp, CancellationToken cancellationToken)
	{
		var nowUtc = DateTime.UtcNow;
		string refreshTokenValue = GenerateOpaqueRefreshToken();
		var refreshToken = new RefreshToken
		{
			UserId = user.Id,
			TokenHash = ComputeRefreshTokenHash(refreshTokenValue),
			FamilyId = Guid.NewGuid(),
			CreatedUtc = nowUtc,
			ExpiresUtc = nowUtc.AddDays(_jwtOptions.RefreshTokenDays),
			CreatedByIp = createdByIp
		};

		_dbContext.RefreshTokens.Add(refreshToken);
		await _dbContext.SaveChangesAsync(cancellationToken);

		return CreateTokenPair(user, refreshTokenValue, nowUtc);
	}

	public async Task<RefreshTokenExchangeResult> RotateAsync(string refreshToken, string? createdByIp, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(refreshToken))
		{
			throw new ArgumentException("Refresh token is required.", nameof(refreshToken));
		}

		string presentedTokenHash = ComputeRefreshTokenHash(refreshToken);
		var nowUtc = DateTime.UtcNow;
		await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

		var currentToken = await LockRefreshTokenByHashAsync(presentedTokenHash, cancellationToken);
		if (currentToken is null || currentToken.ExpiresUtc <= nowUtc || currentToken.RevokedUtc.HasValue)
		{
			await transaction.CommitAsync(cancellationToken);
			return RefreshTokenExchangeResult.Failed(RefreshTokenExchangeFailure.Invalid);
		}

		if (currentToken.ConsumedUtc.HasValue)
		{
			if (IsWithinRetryGraceWindow(currentToken.ConsumedUtc.Value, nowUtc)
				&& currentToken.ReplacedByTokenId.HasValue)
			{
				var replacementToken = await LockRefreshTokenByIdAsync(currentToken.ReplacedByTokenId.Value, cancellationToken);
				if (replacementToken is not null
					&& replacementToken.ExpiresUtc > nowUtc
					&& !replacementToken.ConsumedUtc.HasValue
					&& !replacementToken.RevokedUtc.HasValue)
				{
					// Live child within the grace window: benign retry. Never revoke the family;
					// signal the client to retry with the token pair it already holds.
					await transaction.CommitAsync(cancellationToken);
					return RefreshTokenExchangeResult.Failed(RefreshTokenExchangeFailure.RetryInProgress);
				}
			}

			await RevokeFamilyAsync(currentToken.FamilyId, nowUtc, cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			return RefreshTokenExchangeResult.Failed(RefreshTokenExchangeFailure.ReuseDetected);
		}

		currentToken.ConsumedUtc = nowUtc;

		string nextRefreshTokenValue = GenerateOpaqueRefreshToken();
		var nextRefreshToken = new RefreshToken
		{
			UserId = currentToken.UserId,
			TokenHash = ComputeRefreshTokenHash(nextRefreshTokenValue),
			FamilyId = currentToken.FamilyId,
			CreatedUtc = nowUtc,
			ExpiresUtc = nowUtc.AddDays(_jwtOptions.RefreshTokenDays),
			CreatedByIp = createdByIp
		};

		_dbContext.RefreshTokens.Add(nextRefreshToken);
		await _dbContext.SaveChangesAsync(cancellationToken);

		currentToken.ReplacedByTokenId = nextRefreshToken.Id;
		await _dbContext.SaveChangesAsync(cancellationToken);

		var user = await _dbContext.Users.SingleAsync(entity => entity.Id == currentToken.UserId, cancellationToken);
		var issuedPair = CreateTokenPair(user, nextRefreshTokenValue, nowUtc);

		await transaction.CommitAsync(cancellationToken);
		return RefreshTokenExchangeResult.Success(issuedPair);
	}

	public async Task<Guid?> RevokeFamilyAsync(string refreshToken, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(refreshToken))
		{
			throw new ArgumentException("Refresh token is required.", nameof(refreshToken));
		}

		string tokenHash = ComputeRefreshTokenHash(refreshToken);
		await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

		var token = await LockRefreshTokenByHashAsync(tokenHash, cancellationToken);
		if (token is null)
		{
			await transaction.CommitAsync(cancellationToken);
			return null;
		}

		await RevokeFamilyAsync(token.FamilyId, DateTime.UtcNow, cancellationToken);
		await transaction.CommitAsync(cancellationToken);
		return token.UserId;
	}
	#endregion

	#region Private methods
	private TokenPair CreateTokenPair(User user, string refreshToken, DateTime issuedAtUtc)
	{
		return new TokenPair(
			_tokenService.CreateAccessToken(user),
			refreshToken,
			_tokenService.GetAccessTokenExpiryUtc(issuedAtUtc));
	}

	private async Task<RefreshToken?> LockRefreshTokenByHashAsync(string tokenHash, CancellationToken cancellationToken)
	{
		return await _dbContext.RefreshTokens
			.FromSqlInterpolated(
				$"""SELECT * FROM "RefreshTokens" WHERE "TokenHash" = {tokenHash} FOR UPDATE""")
			.SingleOrDefaultAsync(cancellationToken);
	}

	private async Task<RefreshToken?> LockRefreshTokenByIdAsync(Guid refreshTokenId, CancellationToken cancellationToken)
	{
		return await _dbContext.RefreshTokens
			.FromSqlInterpolated(
				$"""SELECT * FROM "RefreshTokens" WHERE "Id" = {refreshTokenId} FOR UPDATE""")
			.SingleOrDefaultAsync(cancellationToken);
	}

	private async Task RevokeFamilyAsync(Guid familyId, DateTime revokedUtc, CancellationToken cancellationToken)
	{
		await _dbContext.RefreshTokens
			.Where(entity => entity.FamilyId == familyId && !entity.RevokedUtc.HasValue)
			.ExecuteUpdateAsync(
				setters => setters.SetProperty(entity => entity.RevokedUtc, revokedUtc),
				cancellationToken);
	}

	private static bool IsWithinRetryGraceWindow(DateTime consumedUtc, DateTime nowUtc)
	{
		return nowUtc - consumedUtc <= RetryGraceWindow;
	}

	private static string GenerateOpaqueRefreshToken()
	{
		byte[] bytes = new byte[RefreshTokenByteLength];
		RandomNumberGenerator.Fill(bytes);
		return Convert.ToBase64String(bytes);
	}

	private static string ComputeRefreshTokenHash(string refreshToken)
	{
		byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
		return Convert.ToHexString(hashBytes);
	}
	#endregion
}
#endregion