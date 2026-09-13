using ChoNaBojo.Server.Data;
using ChoNaBojo.Server.Events;
using ChoNaBojo.Server.Push;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace ChoNaBojo.Server.IntegrationTests.Infrastructure;

public sealed class ChoNaBojoApiFactory : WebApplicationFactory<Program>
{
	#region Public constants
	public const string ApiApplicationName = "ChoNaBojo.Integration.Api";
	#endregion

	#region Private fields
	private readonly string _apiConnectionString;
	#endregion

	#region Constructors
	public ChoNaBojoApiFactory(string connectionString)
	{
		var connectionStringBuilder = new NpgsqlConnectionStringBuilder(
			connectionString)
		{
			ApplicationName = ApiApplicationName
		};
		_apiConnectionString = connectionStringBuilder.ConnectionString;
	}
	#endregion

	#region Public methods
	public HttpClient CreateHttpsClient()
	{
		return CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false,
			BaseAddress = new Uri("https://localhost")
		});
	}
	#endregion

	#region Overrides
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		builder.UseEnvironment("Testing");
		foreach ((string key, string? value) in
			TestConfiguration.Create(_apiConnectionString))
		{
			if (value is not null)
			{
				builder.UseSetting(key, value);
			}
		}

		builder.ConfigureServices(services =>
		{
			services.RemoveAll<ChoNaBojoContext>();
			services.RemoveAll<DbContextOptions<ChoNaBojoContext>>();
			services.AddDbContext<ChoNaBojoContext>(options =>
				options.UseNpgsql(
					_apiConnectionString,
					npgsql => npgsql.UseNetTopologySuite()));

			ServiceDescriptor[] workerDescriptors = services
				.Where(descriptor =>
					descriptor.ServiceType == typeof(IHostedService)
						&& (descriptor.ImplementationType
								== typeof(PushDeliveryWorker)
							|| descriptor.ImplementationType
								== typeof(EventAutoCloseWorker)))
				.ToArray();
			foreach (ServiceDescriptor descriptor in workerDescriptors)
			{
				services.Remove(descriptor);
			}
		});
	}
	#endregion
}
