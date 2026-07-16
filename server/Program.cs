using System.Text;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ChoNaBojo.Server.Auth;
using ChoNaBojo.Server.Data;
using ChoNaBojo.Server.Data.Seeding;

var builder = WebApplication.CreateBuilder(args);

// Bind to Railway's PORT
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
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
var warsawVenuesCsvPath = builder.Configuration["DataSeeding:WarsawVenuesCsvPath"];

// Register the data-layer DbContext (Supabase Postgres via Npgsql + PostGIS/NetTopologySuite).
// Runtime uses the transaction-mode pooler string (AppDb, port 6543); the app never auto-migrates.
builder.Services.AddDbContext<ChoNaBojoContext>(opt =>
    opt.UseNpgsql(
        builder.Configuration.GetConnectionString("AppDb"),
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
builder.Services.AddMemoryCache();
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

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("HealthCheck");

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
