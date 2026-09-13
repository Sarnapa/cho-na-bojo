namespace ChoNaBojo.Server.IntegrationTests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationTestCollection :
	ICollectionFixture<PostgisFixture>
{
	public const string Name = "PostGIS integration tests";
}
