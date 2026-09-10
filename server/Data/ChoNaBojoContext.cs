using ChoNaBojo.Contracts.Consts;
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

	public DbSet<SportsEvent> SportsEvents
	{
		get
		{
			return Set<SportsEvent>();
		}
	}

	public DbSet<EventJoinRequest> EventJoinRequests
	{
		get
		{
			return Set<EventJoinRequest>();
		}
	}

	public DbSet<PushInstallation> PushInstallations
	{
		get
		{
			return Set<PushInstallation>();
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

		modelBuilder.Entity<SportsEvent>(entity =>
		{
			entity.ToTable(tableBuilder =>
			{
				tableBuilder.HasCheckConstraint(
					"CK_SportsEvents_ClientRequestId_NotEmpty",
					"""
					"ClientRequestId" <> '00000000-0000-0000-0000-000000000000'::uuid
					""");

				tableBuilder.HasCheckConstraint(
					"CK_SportsEvents_Title_NotBlank",
					"""
					BTRIM("Title") <> ''
					""");

				tableBuilder.HasCheckConstraint(
					"CK_SportsEvents_Description_NotBlank",
					"""
					"Description" IS NULL OR BTRIM("Description") <> ''
					""");

				tableBuilder.HasCheckConstraint(
					"CK_SportsEvents_ParticipantLimit",
					$"""
					"ParticipantLimit" BETWEEN {EventPolicy.ParticipantLimitMinimum} AND {EventPolicy.ParticipantLimitMaximum}
					""");

				tableBuilder.HasCheckConstraint(
					"CK_SportsEvents_TimeRange",
					$"""
					"EstimatedEndsAtUtc" > "StartsAtUtc"
					AND "EstimatedEndsAtUtc" <= "StartsAtUtc" + INTERVAL '{EventPolicy.MaximumDuration.TotalHours:0} hours'
					""");
			});

			entity.HasKey(sportsEvent => sportsEvent.Id);
			entity.Property(sportsEvent => sportsEvent.Id)
				.HasDefaultValueSql("gen_random_uuid()");
			entity.Property(sportsEvent => sportsEvent.Title)
				.IsRequired()
				.HasMaxLength(EventPolicy.TitleMaxLength);
			entity.Property(sportsEvent => sportsEvent.Description)
				.HasMaxLength(EventPolicy.DescriptionMaxLength);
			entity.Property(sportsEvent => sportsEvent.StartsAtUtc)
				.IsRequired()
				.HasColumnType("timestamp with time zone");
			entity.Property(sportsEvent => sportsEvent.EstimatedEndsAtUtc)
				.IsRequired()
				.HasColumnType("timestamp with time zone");
			entity.Property(sportsEvent => sportsEvent.CreatedUtc)
				.IsRequired()
				.HasColumnType("timestamp with time zone");

			entity.HasIndex(sportsEvent => new
				{
					sportsEvent.OrganizerUserId,
					sportsEvent.ClientRequestId
				})
				.IsUnique();
			entity.HasIndex(sportsEvent => new
				{
					sportsEvent.VenueId,
					sportsEvent.EstimatedEndsAtUtc
				});

			entity.HasOne(sportsEvent => sportsEvent.Organizer)
				.WithMany(user => user.OrganizedEvents)
				.HasForeignKey(sportsEvent => sportsEvent.OrganizerUserId)
				.OnDelete(DeleteBehavior.Restrict);

			entity.HasOne(sportsEvent => sportsEvent.VenueSport)
				.WithMany(venueSport => venueSport.SportsEvents)
				.HasForeignKey(sportsEvent => new
				{
					sportsEvent.VenueId,
					sportsEvent.SportId
				})
				.OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<EventJoinRequest>(entity =>
		{
			entity.ToTable(tableBuilder =>
			{
				tableBuilder.HasCheckConstraint(
					"CK_EventJoinRequests_Status",
					"""
					"Status" IN (1, 2, 3)
					""");

				tableBuilder.HasCheckConstraint(
					"CK_EventJoinRequests_StatusUpdatedUtc",
					"""
					(
						"Status" = 1 AND "UpdatedUtc" IS NULL
					)
					OR
					(
						"Status" <> 1 AND "UpdatedUtc" IS NOT NULL
					)
					""");
			});

			entity.HasKey(request => request.Id);
			entity.Property(request => request.Id)
				.HasDefaultValueSql("gen_random_uuid()");
			entity.Property(request => request.Status)
				.IsRequired();
			entity.Property(request => request.CreatedUtc)
				.IsRequired()
				.HasColumnType("timestamp with time zone");
			entity.Property(request => request.UpdatedUtc)
				.HasColumnType("timestamp with time zone");

			entity.HasIndex(request => new
				{
					request.SportsEventId,
					request.RequesterUserId
				})
				.IsUnique();
			entity.HasIndex(request => new
				{
					request.SportsEventId,
					request.Status
				});

			entity.HasOne(request => request.SportsEvent)
				.WithMany(sportsEvent => sportsEvent.EventJoinRequests)
				.HasForeignKey(request => request.SportsEventId)
				.OnDelete(DeleteBehavior.Cascade);

			entity.HasOne(request => request.Requester)
				.WithMany(user => user.EventJoinRequests)
				.HasForeignKey(request => request.RequesterUserId)
				.OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<PushInstallation>(entity =>
		{
			entity.ToTable(tableBuilder =>
			{
				tableBuilder.HasCheckConstraint(
					"CK_PushInstallations_DeviceRegistrationId_NotBlank",
					"""
					BTRIM("DeviceRegistrationId") <> ''
					""");

				tableBuilder.HasCheckConstraint(
					"CK_PushInstallations_LastSeenUtc",
					"""
					"LastSeenUtc" >= "CreatedUtc"
					""");
			});

			entity.HasKey(installation => installation.Id);
			entity.Property(installation => installation.Id)
				.HasDefaultValueSql("gen_random_uuid()");
			entity.Property(installation => installation.DeviceRegistrationId)
				.IsRequired()
				.HasMaxLength(PushPolicy.DeviceRegistrationIdMaxLength);
			entity.Property(installation => installation.AppVersion)
				.HasMaxLength(PushPolicy.AppVersionMaxLength);
			entity.Property(installation => installation.CreatedUtc)
				.IsRequired()
				.HasColumnType("timestamp with time zone");
			entity.Property(installation => installation.LastSeenUtc)
				.IsRequired()
				.HasColumnType("timestamp with time zone");
			entity.Property(installation => installation.DisabledUtc)
				.HasColumnType("timestamp with time zone");

			entity.HasIndex(installation => installation.DeviceRegistrationId)
				.IsUnique();
			entity.HasIndex(installation => installation.UserId);

			entity.HasOne(installation => installation.User)
				.WithMany()
				.HasForeignKey(installation => installation.UserId)
				.OnDelete(DeleteBehavior.Cascade);
		});
	}
	#endregion
}
