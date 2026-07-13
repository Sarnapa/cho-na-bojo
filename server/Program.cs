using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using ChoNaBojo.Server.Data;

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

// Register the data-layer DbContext (Supabase Postgres via Npgsql + PostGIS/NetTopologySuite).
// Runtime uses the transaction-mode pooler string (AppDb, port 6543); the app never auto-migrates.
builder.Services.AddDbContext<ChoNaBojoContext>(opt =>
    opt.UseNpgsql(
        builder.Configuration.GetConnectionString("AppDb"),
        npgsql => npgsql.UseNetTopologySuite()));

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
