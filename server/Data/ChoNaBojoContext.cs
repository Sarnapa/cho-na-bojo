using ChoNaBojo.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChoNaBojo.Server.Data;

/// <summary>
/// EF Core context for the F-01 data-layer foundation: reference tables <see cref="Sports"/>,
/// <see cref="Venues"/>, and the <see cref="VenueSports"/> join. PostGIS-backed; the app never
/// auto-migrates (schema is applied via <c>dotnet ef database update</c>).
/// </summary>
public class ChoNaBojoContext(DbContextOptions<ChoNaBojoContext> options): DbContext(options)
{
	public DbSet<Sport> Sports
	{
		get
		{
			return Set<Sport>();
		}
	}

	public DbSet<Venue> Venues
	{
		get
		{
			return Set<Venue>();
		}
	}

	public DbSet<VenueSport> VenueSports
	{
		get
		{
			return Set<VenueSport>();
		}
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		// Emit CREATE EXTENSION postgis in the migration so geometry columns / spatial ops work.
		modelBuilder.HasPostgresExtension("postgis");

		modelBuilder.Entity<Sport>(entity =>
		{
			entity.HasKey(s => s.Id);
			entity.Property(s => s.Id).ValueGeneratedNever();
			entity.Property(s => s.Code).IsRequired();
			entity.Property(s => s.Name).IsRequired();
			entity.HasIndex(s => s.Code).IsUnique();

			entity.HasData(
				new Sport { Id = 1, Code = "football", Name = "Piłka nożna" },
				new Sport { Id = 2, Code = "basketball", Name = "Koszykówka" },
				new Sport { Id = 3, Code = "volleyball", Name = "Siatkówka" },
				new Sport { Id = 4, Code = "tennis", Name = "Tenis" },
				new Sport { Id = 5, Code = "running", Name = "Bieganie" },
				new Sport { Id = 6, Code = "cycling", Name = "Kolarstwo" },
				new Sport { Id = 7, Code = "rollerblading", Name = "Jazda na rolkach" },
				new Sport { Id = 8, Code = "gym", Name = "Siłownia" },
				new Sport { Id = 9, Code = "street_workout", Name = "Street workout" },
				new Sport { Id = 10, Code = "swimming", Name = "Pływanie" }
			);
		});

		modelBuilder.Entity<Venue>(entity =>
		{
			entity.HasKey(v => v.Id);
			// Venue ids are the stable CSV identity, supplied by the seeder — never generated.
			entity.Property(v => v.Id).ValueGeneratedNever();
			entity.Property(v => v.Name).IsRequired();
			entity.Property(v => v.Address).IsRequired();
			entity.Property(v => v.Description).IsRequired();
			entity.Property(v => v.Location)
							.HasColumnType("geometry(Point,4326)")
							.IsRequired();

			// GiST spatial index powers the S-02 map-viewport bbox queries.
			entity.HasIndex(v => v.Location).HasMethod("gist");
		});

		modelBuilder.Entity<VenueSport>(entity =>
		{
			entity.HasKey(vs => new { vs.VenueId, vs.SportId });

			entity.HasOne(vs => vs.Venue)
							.WithMany(v => v.VenueSports)
							.HasForeignKey(vs => vs.VenueId)
							.OnDelete(DeleteBehavior.Cascade);

			entity.HasOne(vs => vs.Sport)
							.WithMany(s => s.VenueSports)
							.HasForeignKey(vs => vs.SportId)
							.OnDelete(DeleteBehavior.Cascade);
		});
	}
}
