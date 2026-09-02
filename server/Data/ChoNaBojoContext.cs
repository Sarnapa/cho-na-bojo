using ChoNaBojo.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChoNaBojo.Server.Data;

/// <summary>
/// EF Core context for the F-01 data-layer foundation: reference tables <see cref="Sports"/>,
/// <see cref="Venues"/>, and the <see cref="VenueSports"/> join. F-02 adds <see cref="Users"/>
/// and <see cref="RefreshTokens"/> for self-hosted auth. PostGIS-backed; the app never
/// auto-migrates (schema is applied via <c>dotnet ef database update</c>).
/// </summary>
public class ChoNaBojoContext(DbContextOptions<ChoNaBojoContext> options): DbContext(options)
{
	#region Public properties
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

	public DbSet<User> Users
	{
		get
		{
			return Set<User>();
		}
	}

	public DbSet<RefreshToken> RefreshTokens
	{
		get
		{
			return Set<RefreshToken>();
		}
	}
	#endregion

	#region Overrides
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		// Emit CREATE EXTENSION postgis in the migration so geometry columns / spatial ops work.
		modelBuilder.HasPostgresExtension("postgis");
		modelBuilder.HasPostgresExtension("pgcrypto");

		modelBuilder.Entity<Sport>(entity =>
		{
			entity.HasKey(s => s.Id);
			entity.Property(s => s.Id).ValueGeneratedNever();
			entity.Property(s => s.Code).IsRequired();
			entity.Property(s => s.Name).IsRequired();
			entity.HasIndex(s => s.Code).IsUnique();

			entity.HasData(
				new Sport { Id = 1, Code = "football", Name = "Football" },
				new Sport { Id = 2, Code = "basketball", Name = "Basketball" },
				new Sport { Id = 3, Code = "volleyball", Name = "Volleyball" },
				new Sport { Id = 4, Code = "tennis", Name = "Tennis" },
				new Sport { Id = 5, Code = "running", Name = "Running" },
				new Sport { Id = 6, Code = "cycling", Name = "Cycling" },
				new Sport { Id = 7, Code = "rollerblading", Name = "Rollerblading" },
				new Sport { Id = 8, Code = "gym", Name = "Gym" },
				new Sport { Id = 9, Code = "street_workout", Name = "Street Workout" },
				new Sport { Id = 10, Code = "swimming", Name = "Swimming" }
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

		modelBuilder.Entity<User>(entity =>
		{
			entity.ToTable(tableBuilder =>
			{
				tableBuilder.HasCheckConstraint(
					"CK_Users_ContactMethod",
					"""
					(
						(NULLIF(BTRIM("ContactPhone"), '') IS NOT NULL)
						OR (NULLIF(BTRIM("ContactEmail"), '') IS NOT NULL)
						OR ("CommunicatorPlatform" IS NOT NULL AND NULLIF(BTRIM("CommunicatorHandle"), '') IS NOT NULL)
					)
					AND
					(
						("CommunicatorPlatform" IS NULL AND NULLIF(BTRIM("CommunicatorHandle"), '') IS NULL)
						OR ("CommunicatorPlatform" IS NOT NULL AND NULLIF(BTRIM("CommunicatorHandle"), '') IS NOT NULL)
					)
					""");

				tableBuilder.HasCheckConstraint(
					"CK_Users_CommunicatorPlatform",
					"""
					"CommunicatorPlatform" IS NULL OR "CommunicatorPlatform" IN (1, 2, 3)
					""");
			});

			entity.HasKey(user => user.Id);
			entity.Property(user => user.Id)
				.HasDefaultValueSql("gen_random_uuid()");

			entity.Property(user => user.LoginEmail)
				.IsRequired()
				.HasMaxLength(320);
			entity.Property(user => user.NormalizedLoginEmail)
				.IsRequired()
				.HasMaxLength(320);
			entity.Property(user => user.PasswordHash)
				.IsRequired()
				.HasMaxLength(512);
			entity.Property(user => user.ContactPhone)
				.HasMaxLength(32);
			entity.Property(user => user.ContactEmail)
				.HasMaxLength(320);
			entity.Property(user => user.CommunicatorHandle)
				.HasMaxLength(100);
			entity.Property(user => user.CreatedUtc)
				.IsRequired();
			entity.Property(user => user.UpdatedUtc)
				.IsRequired();

			entity.HasIndex(user => user.NormalizedLoginEmail)
				.IsUnique();
		});

		modelBuilder.Entity<RefreshToken>(entity =>
		{
			entity.HasKey(token => token.Id);
			entity.Property(token => token.TokenHash)
				.IsRequired()
				.HasMaxLength(64);
			entity.Property(token => token.CreatedUtc)
				.IsRequired();
			entity.Property(token => token.ExpiresUtc)
				.IsRequired();

			entity.HasIndex(token => token.TokenHash)
				.IsUnique();
			entity.HasIndex(token => token.FamilyId);

			entity.HasOne(token => token.User)
				.WithMany(user => user.RefreshTokens)
				.HasForeignKey(token => token.UserId)
				.OnDelete(DeleteBehavior.Cascade);
		});
	}
	#endregion
}
