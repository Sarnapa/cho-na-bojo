using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using ChoNaBojo.Server.Auth;
using ChoNaBojo.Server.Data;
using ChoNaBojo.Server.Data.Seeding;

var builder = WebApplication.CreateBuilder(args);
string appDbConnectionString = ResolveRuntimeAppDbConnectionString(builder.Configuration.GetConnectionString("AppDb"));

// Bind to Railway's PORT
string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
if (!builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Configure forwarded headers for Railway's TLS-terminating proxy
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
string? warsawVenuesCsvPath = builder.Configuration["DataSeeding:WarsawVenuesCsvPath"];

// Register the data-layer DbContext (Supabase Postgres via Npgsql + PostGIS/NetTopologySuite).
// Runtime uses the transaction-mode pooler string (AppDb, port 6543); the app never auto-migrates.
builder.Services.AddDbContext<ChoNaBojoContext>(opt =>
    opt.UseNpgsql(
        appDbConnectionString,
        npgsql => npgsql.UseNetTopologySuite())
    .UseSeeding((context, _) => WarsawVenueSeeder.Seed(context, warsawVenuesCsvPath))
    .UseAsyncSeeding((context, _, cancellationToken) =>
        WarsawVenueSeeder.SeedAsync(context, warsawVenuesCsvPath, cancellationToken)));

builder.Services
	.AddOptions<JwtOptions>()
	.Bind(builder.Configuration.GetRequiredSection(JwtOptions.SectionName))
	.ValidateDataAnnotations()
	.Validate(
		options => Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
		$"{JwtOptions.SectionName}:SigningKey must be at least 256 bits (32 bytes).")
	.ValidateOnStart();

builder.Services
	.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
	.AddJwtBearer();

builder.Services
	.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
	.Configure<IOptions<JwtOptions>>((jwtBearerOptions, jwtOptionsAccessor) =>
	{
		var jwtOptions = jwtOptionsAccessor.Value;
		var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

		jwtBearerOptions.TokenValidationParameters = new TokenValidationParameters
		{
			ValidateIssuer = true,
			ValidIssuer = jwtOptions.Issuer,
			ValidateAudience = true,
			ValidAudience = jwtOptions.Audience,
			ValidateIssuerSigningKey = true,
			IssuerSigningKey = signingKey,
			ValidateLifetime = true,
			ClockSkew = TimeSpan.FromSeconds(30)
		};
	});

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
	options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
	options.AddPolicy(AuthEndpoints.RateLimiterPolicyName, httpContext =>
		RateLimitPartition.GetFixedWindowLimiter(
			partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
			factory: _ => new FixedWindowRateLimiterOptions
			{
				PermitLimit = 10,
				Window = TimeSpan.FromMinutes(1),
				QueueLimit = 0
			}));
});
builder.Services.AddSingleton<IPasswordService, PasswordService>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();

var app = builder.Build();

// Enable forwarded headers so HTTPS redirection works behind Railway's proxy
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("HealthCheck");

app.MapAuthEndpoints();

// Protected domain seam: future feature endpoints (S-03+) map onto this group to inherit authorization.
var apiGroup = app.MapGroup("/api")
	.RequireAuthorization();

app.Run();

static string ResolveRuntimeAppDbConnectionString(string? connectionString)
{
	if (string.IsNullOrWhiteSpace(connectionString))
	{
		throw new InvalidOperationException("Connection string 'AppDb' is required.");
	}

	var builder = new NpgsqlConnectionStringBuilder(connectionString);

	// Supabase transaction pooler (port 6543) is already pooling server-side.
	// Disabling client pooling prevents stale pooled sockets causing write timeouts.
	if (builder.Port == 6543 && builder.Pooling)
	{
		builder.Pooling = false;
	}

	return builder.ConnectionString;
}
