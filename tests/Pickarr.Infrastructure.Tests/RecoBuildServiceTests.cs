using Hangfire;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Domain;
using Pickarr.Core.Domain.Entities;
using Pickarr.Core.Recommendations;
using Pickarr.Core.Settings;
using Pickarr.Infrastructure.Catalog;
using Pickarr.Infrastructure.Jobs;
using Pickarr.Infrastructure.Metadata.Reco;
using Pickarr.Infrastructure.Persistence;
using Shouldly;

namespace Pickarr.Infrastructure.Tests;

public sealed class RecoBuildServiceTests : IAsyncLifetime
{
    private SqliteConnection? _connection;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Jobs_support_catalogs_larger_than_sqlite_variable_limit(bool jsonCollections)
    {
        var (db, build, sourceId, profileId) = await SeedL1Async(jsonCollections: jsonCollections);
        var now = DateTime.UtcNow;
        var ids = Enumerable.Range(1000, 120).ToArray();
        db.Titles.AddRange(ids.Select(id => Title(id, 7, 100, 10)));
        db.Availabilities.AddRange(ids.Select(id => new Availability
        {
            TitleId = id,
            CatalogSourceId = sourceId,
            FirstSeenAt = now,
            LastSeenAt = now,
        }));
        db.Keywords.AddRange(ids.Select(id => new Keyword { Id = id, Name = $"Keyword {id}" }));
        db.TitleKeywords.AddRange(ids.Select(id => new TitleKeyword { TitleId = 101, KeywordId = id }));
        db.People.AddRange(ids.Select(id => new Person { Id = id, Name = $"Person {id}" }));
        db.TitleCredits.AddRange(ids.Select(id => new TitleCredit
        {
            TitleId = 101,
            PersonId = id,
            Role = CreditRoles.Cast,
            Order = id,
        }));
        db.Ratings.Add(new Rating { ProfileId = profileId, TitleId = 101, Value = RatingValues.Like, RatedAt = now });
        db.TitleTmdbRecommendations.Add(new TitleTmdbRecommendation { SourceTitleId = 101, RelatedTitleId = 102, Rank = 1 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        SQLitePCL.raw.sqlite3_limit(_connection!.Handle, SQLitePCL.raw.SQLITE_LIMIT_VARIABLE_NUMBER, 80);

        var models = Substitute.For<IRecoModelService>();
        models.IsReady.Returns(true);
        models.GetInfoAsync(Arg.Any<CancellationToken>()).Returns(new RecoModelInfoDto("ready", "test", null, null, null));
        models.TryEmbedBatch(Arg.Any<IReadOnlyList<string>>()).Returns(call =>
            call.Arg<IReadOnlyList<string>>().Select(_ => new[] { 1f, 0f }).ToList());
        var embedding = new RecoEmbeddingService(db, models, new EmptySettingsStore(new Dictionary<string, string>
        {
            [SettingsKeys.RecoEmbeddingsThrottle] = "false",
        }), new FixedClock(now), Microsoft.Extensions.Logging.Abstractions.NullLogger<RecoEmbeddingService>.Instance);

        (await embedding.EmbedPendingAsync()).ShouldBe(123);
        (await embedding.EmbedPendingAsync()).ShouldBe(0);
        var state = await db.RecoStates.SingleAsync();
        state.EmbeddingCoverage.ShouldBe(1);
        state.ModelStatus = RecoModelStatuses.Ready;
        await db.SaveChangesAsync();
        (await build.BuildAsync(profileId)).ShouldBe(1);
        (await db.Recommendations.CountAsync()).ShouldBe(123);
    }

    [Fact]
    public void RecoBuildJob_is_queued_on_ml()
    {
        var method = typeof(RecoBuildJob).GetMethod(nameof(RecoBuildJob.ExecuteAsync));
        method.ShouldNotBeNull();
        var queue = method!.GetCustomAttributes(typeof(QueueAttribute), inherit: true)
            .Cast<QueueAttribute>()
            .Single();
        queue.Queue.ShouldBe("ml");
    }

    [Fact]
    public async Task Build_publishes_version_includes_removed_excludes_disabled_and_sets_cold()
    {
        var (db, build) = await SeedAsync();

        var v1 = await build.BuildAsync();
        v1.ShouldBe(1);

        var state = await db.RecoStates.SingleAsync();
        state.CurrentVersion.ShouldBe(1);
        state.Stage.ShouldBe(RecoLearning.StageCold);
        state.EffectiveN.ShouldBe(0);
        state.BuiltAt.ShouldNotBeNull();

        var rows = await db.Recommendations.Where(r => r.Version == 1).OrderBy(r => r.Rank).ToListAsync();
        rows.Select(r => r.TitleId).ShouldBe([20, 10]); // higher prior (title 20) first
        rows.ShouldAllBe(r => r.Personal == null && r.ReasonsJson == RecoReasons.PopularOnlyJson);

        // Title 30 only on disabled source → excluded; title 40 has zero availability → excluded
        rows.ShouldNotContain(r => r.TitleId == 30);
        rows.ShouldNotContain(r => r.TitleId == 40);

        var v2 = await build.BuildAsync();
        v2.ShouldBe(2);
        (await db.Recommendations.CountAsync(r => r.Version == 1)).ShouldBe(0);
        (await db.Recommendations.CountAsync(r => r.Version == 2)).ShouldBe(2);
    }

    [Fact]
    public async Task Build_L1_ranks_matching_genre_above_others_and_writes_reasons()
    {
        var (db, build, enabledSourceId, adminId) = await SeedL1Async();

        db.Ratings.Add(new Rating
        {
            ProfileId = adminId,
            TitleId = 101,
            Value = RatingValues.Like,
            RatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await build.BuildAsync();

        var state = await db.RecoStates.SingleAsync();
        state.EffectiveN.ShouldBe(1);
        state.Stage.ShouldBe(RecoLearning.StageCold); // N_eff < 5
        state.ProfileJson.ShouldNotBeNull();

        var rows = await db.Recommendations.Where(r => r.Version == state.CurrentVersion)
            .OrderBy(r => r.Rank)
            .ToListAsync();
        // Liked sci-fi (101) may rank high; other sci-fi (102) should beat drama (103).
        var rank102 = rows.Single(r => r.TitleId == 102).Rank;
        var rank103 = rows.Single(r => r.TitleId == 103).Rank;
        rank102.ShouldBeLessThan(rank103);

        var sciFiRow = rows.Single(r => r.TitleId == 102);
        sciFiRow.Personal.ShouldNotBeNull();
        sciFiRow.ReasonsJson.ShouldContain(RecoReasons.SimilarTo);
        sciFiRow.ReasonsJson.ShouldContain(RecoReasons.Terms);

        // Source id used only to keep seed API symmetric for similar tests later.
        enabledSourceId.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Build_trains_level3_without_each_rated_titles_own_signal()
    {
        var model = new CapturingLevel3Model();
        var (db, build, _, adminId) = await SeedL1Async(level3Model: model);
        db.Ratings.AddRange(
            new Rating
            {
                ProfileId = adminId,
                TitleId = 101,
                Value = RatingValues.Like,
                RatedAt = DateTime.UtcNow,
            },
            new Rating
            {
                ProfileId = adminId,
                TitleId = 103,
                Value = RatingValues.Dislike,
                RatedAt = DateTime.UtcNow,
            });
        db.RecoStates.Add(new RecoState
        {
            Id = RecoState.SingletonId,
            ProfileId = adminId,
            Level3Active = true,
        });
        await db.SaveChangesAsync();

        await build.BuildAsync(adminId);
        var firstFeatures = model.Calls[0].Single(example => example.TitleId == 101).Features;

        var target = await db.Ratings.SingleAsync(r => r.TitleId == 101);
        target.Value = RatingValues.Dislike;
        await db.SaveChangesAsync();
        await build.BuildAsync(adminId);
        var secondFeatures = model.Calls[1].Single(example => example.TitleId == 101).Features;

        secondFeatures.ShouldBe(firstFeatures);
    }

    [Fact]
    public async Task Build_tmdb_edge_from_like_lifts_related_title()
    {
        var (db, build, _, adminId) = await SeedL1Async(new EmptySettingsStore(new Dictionary<string, string>
        {
            [SettingsKeys.RecoTmdbAmplifierMaxShare] = "0.5",
        }));

        // Equal sparse features for 102 and 103 so only the TMDB hop decides between them.
        var dramaLink = await db.TitleGenres.SingleAsync(tg => tg.TitleId == 103 && tg.GenreId == 18);
        db.TitleGenres.Remove(dramaLink);
        db.TitleGenres.Add(new TitleGenre { TitleId = 103, GenreId = 878 });
        db.Ratings.Add(new Rating
        {
            ProfileId = adminId,
            TitleId = 101,
            Value = RatingValues.Like,
            RatedAt = DateTime.UtcNow,
        });
        db.TitleTmdbRecommendations.Add(new TitleTmdbRecommendation
        {
            SourceTitleId = 101,
            RelatedTitleId = 103,
            Rank = 1,
        });
        await db.SaveChangesAsync();

        await build.BuildAsync();

        var rows = await db.Recommendations.Where(r => r.Version == 1).ToListAsync();
        var related = rows.Single(r => r.TitleId == 103);
        var sibling = rows.Single(r => r.TitleId == 102);
        related.Personal.ShouldNotBeNull();
        sibling.Personal.ShouldNotBeNull();
        related.Personal!.Value.ShouldBeGreaterThan(sibling.Personal!.Value);
        related.Rank.ShouldBeLessThan(sibling.Rank);
        related.ReasonsJson.ShouldContain(RecoReasons.SimilarTo);
    }

    [Fact]
    public async Task Build_cold_profile_lifts_another_profiles_love_and_keeps_half_the_prior()
    {
        var (db, build, _, adminId) = await SeedL1Async();
        var other = new Profile { Name = "Other", Color = "teal", CreatedAt = DateTime.UtcNow };
        db.Profiles.Add(other);
        await db.SaveChangesAsync();
        db.Ratings.Add(new Rating
        {
            ProfileId = other.Id,
            TitleId = 103,
            Value = RatingValues.Love,
            RatedAt = DateTime.UtcNow,
        });
        db.Favorites.Add(new Favorite { ProfileId = other.Id, TitleId = 101, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        await build.BuildAsync(adminId);

        var rows = await db.Recommendations.Where(r => r.ProfileId == adminId).ToListAsync();
        rows.ShouldAllBe(r => r.Personal == null && r.ReasonsJson == RecoReasons.PopularOnlyJson);

        var loved = rows.Single(r => r.TitleId == 103);
        var neighbour = rows.Single(r => r.TitleId == 101);
        loved.Score.ShouldBe(0.5 * loved.Prior + 0.5, 1e-9);
        neighbour.Score.ShouldBe(0.5 * neighbour.Prior, 1e-9);
        loved.Score.ShouldBeGreaterThan(loved.Prior);
        loved.Rank.ShouldBeLessThan(neighbour.Rank);
    }

    [Fact]
    public async Task Build_cold_profile_ignores_another_profiles_dislike()
    {
        var (db, build, _, adminId) = await SeedL1Async();
        var other = new Profile { Name = "Other", Color = "teal", CreatedAt = DateTime.UtcNow };
        db.Profiles.Add(other);
        await db.SaveChangesAsync();
        db.Ratings.Add(new Rating
        {
            ProfileId = other.Id,
            TitleId = 103,
            Value = RatingValues.Dislike,
            RatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await build.BuildAsync(adminId);

        var rows = await db.Recommendations.Where(r => r.ProfileId == adminId).ToListAsync();
        rows.ShouldAllBe(r => Math.Abs(r.Score - r.Prior) < 1e-9);
        rows.OrderBy(r => r.Rank).Select(r => r.TitleId).ShouldBe([101, 102, 103]);
    }

    [Fact]
    public async Task Build_at_n0_drops_the_household_term()
    {
        var (db, build, _, adminId) = await SeedL1Async(new EmptySettingsStore(new Dictionary<string, string>
        {
            [SettingsKeys.RecoN0] = "1",
        }));
        var other = new Profile { Name = "Other", Color = "teal", CreatedAt = DateTime.UtcNow };
        db.Profiles.Add(other);
        await db.SaveChangesAsync();
        db.Ratings.AddRange(
            new Rating { ProfileId = adminId, TitleId = 101, Value = RatingValues.Like, RatedAt = DateTime.UtcNow },
            new Rating { ProfileId = other.Id, TitleId = 103, Value = RatingValues.Love, RatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        await build.BuildAsync(adminId);

        var state = await db.RecoStates.SingleAsync(s => s.ProfileId == adminId);
        state.EffectiveN.ShouldBeGreaterThanOrEqualTo(1);

        var rows = await db.Recommendations.Where(r => r.ProfileId == adminId).ToListAsync();
        var personalWeight = RecoLearning.PersonalWeight(state.EffectiveN, n0: 1);
        foreach (var row in rows)
        {
            row.Personal.ShouldNotBeNull();
            row.Score.ShouldBe(RecoLearning.FuseScore(row.Prior, row.Personal!.Value, personalWeight), 1e-9);
        }

        var rank102 = rows.Single(r => r.TitleId == 102).Rank;
        var rank103 = rows.Single(r => r.TitleId == 103).Rank;
        rank102.ShouldBeLessThan(rank103);
    }

    [Fact]
    public async Task Build_L1_dislike_lowers_neighbour_and_explicit_beats_implicit()
    {
        var (db, build, _, adminId) = await SeedL1Async();

        db.Favorites.Add(new Favorite { ProfileId = adminId, TitleId = 103, CreatedAt = DateTime.UtcNow });
        db.Ratings.Add(new Rating { ProfileId = adminId, TitleId = 103, Value = RatingValues.Dislike, RatedAt = DateTime.UtcNow });
        db.Ratings.Add(new Rating { ProfileId = adminId, TitleId = 101, Value = RatingValues.Like, RatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        await build.BuildAsync();
        var state = await db.RecoStates.SingleAsync();
        // Like + Dislike → N_eff = 2 (favorite ignored because 103 has explicit)
        state.EffectiveN.ShouldBe(2);

        var rows = await db.Recommendations.Where(r => r.Version == state.CurrentVersion).ToListAsync();
        var drama = rows.Single(r => r.TitleId == 103);
        var otherSciFi = rows.Single(r => r.TitleId == 102);
        otherSciFi.Score.ShouldBeGreaterThan(drama.Score);
    }

    [Fact]
    public async Task Build_L1_arr_add_survives_delete_audit()
    {
        var (db, build, _, adminId) = await SeedL1Async();
        var now = DateTime.UtcNow;
        db.ArrAuditLogs.AddRange(
            new ArrAuditLog
            {
                ProfileId = adminId,
                At = now,
                Action = ArrAuditActions.Add,
                Kind = ArrKinds.Radarr,
                TitleId = 102,
                Options = "{}",
                Outcome = ArrAuditOutcomes.Success,
            },
            new ArrAuditLog
            {
                ProfileId = adminId,
                At = now.AddMinutes(1),
                Action = ArrAuditActions.Delete,
                Kind = ArrKinds.Radarr,
                TitleId = 102,
                Options = "{}",
                Outcome = ArrAuditOutcomes.Success,
            });
        await db.SaveChangesAsync();

        await build.BuildAsync();
        var state = await db.RecoStates.SingleAsync();
        state.EffectiveN.ShouldBe(0.6);
    }

    [Fact]
    public async Task Build_skips_L2_below_minCoverage_and_uses_L2_when_ready()
    {
        var (db, build, _, adminId) = await SeedL1Async();
        db.Ratings.Add(new Rating
        {
            ProfileId = adminId,
            TitleId = 101,
            Value = RatingValues.Like,
            RatedAt = DateTime.UtcNow,
        });

        // Liked 101 ≈ drama 103 in embedding space; sci-fi 102 is orthogonal.
        SeedUnitEmbedding(db, 101, [1f, 0f]);
        SeedUnitEmbedding(db, 102, [0f, 1f]);
        SeedUnitEmbedding(db, 103, [0.98f, 0.1f]);

        db.RecoStates.Add(new RecoState
        {
            Id = RecoState.SingletonId,
            ProfileId = adminId,
            ModelStatus = RecoModelStatuses.Ready,
            ModelId = RecoModelCatalog.DefaultModelId,
            EmbeddingCoverage = 0.5, // below default minCoverage 0.8
        });
        await db.SaveChangesAsync();

        await build.BuildAsync();
        var below = await db.Recommendations.Where(r => r.Version == 1).ToListAsync();
        below.Single(r => r.TitleId == 102).Rank.ShouldBeLessThan(below.Single(r => r.TitleId == 103).Rank);

        var state = await db.RecoStates.SingleAsync();
        state.EmbeddingCoverage = 1.0;
        await db.SaveChangesAsync();

        await build.BuildAsync();
        var above = await db.Recommendations.Where(r => r.Version == 2).ToListAsync();
        // L2 should pull drama (103) above other sci-fi (102) despite sparse genre affinity.
        above.Single(r => r.TitleId == 103).Rank.ShouldBeLessThan(above.Single(r => r.TitleId == 102).Rank);
    }

    [Fact]
    public async Task Build_keeps_separate_profiles_and_model_state_on_admin()
    {
        var (db, build, _, adminId) = await SeedL1Async(new EmptySettingsStore(new Dictionary<string, string>
        {
            // One Love is N_eff 1.5. At N0 the household term is 0, so each profile's own genre decides the order.
            [SettingsKeys.RecoN0] = "1.5",
        }));
        var other = new Profile { Name = "Other", Color = "teal", CreatedAt = DateTime.UtcNow };
        db.Profiles.Add(other);
        db.Ratings.Add(new Rating
        {
            ProfileId = adminId,
            TitleId = 101,
            Value = RatingValues.Love,
            RatedAt = DateTime.UtcNow,
        });
        db.Ratings.Add(new Rating
        {
            Profile = other,
            TitleId = 103,
            Value = RatingValues.Love,
            RatedAt = DateTime.UtcNow,
        });
        db.RecoStates.Add(new RecoState
        {
            ProfileId = adminId,
            ModelStatus = RecoModelStatuses.Ready,
            ModelId = RecoModelCatalog.DefaultModelId,
            EmbeddingCoverage = 1,
        });
        await db.SaveChangesAsync();

        await build.BuildAsync(adminId);
        await build.BuildAsync(other.Id);

        var adminState = await db.RecoStates.SingleAsync(s => s.ProfileId == adminId);
        var otherState = await db.RecoStates.SingleAsync(s => s.ProfileId == other.Id);
        adminState.ProfileJson.ShouldNotBe(otherState.ProfileJson);
        adminState.ModelStatus.ShouldBe(RecoModelStatuses.Ready);
        adminState.EmbeddingCoverage.ShouldBe(1);
        otherState.ModelStatus.ShouldBe("Absent");
        otherState.EmbeddingCoverage.ShouldBe(0);

        var adminRows = await db.Recommendations.Where(r => r.ProfileId == adminId).ToListAsync();
        var otherRows = await db.Recommendations.Where(r => r.ProfileId == other.Id).ToListAsync();
        adminRows.ShouldAllBe(r => r.ProfileId == adminId);
        adminRows.Single(r => r.TitleId == 102).Rank.ShouldBeLessThan(adminRows.Single(r => r.TitleId == 103).Rank);
        otherRows.Single(r => r.TitleId == 103).Rank.ShouldBeLessThan(otherRows.Single(r => r.TitleId == 102).Rank);
    }

    [Fact]
    public async Task Build_reads_use_implicit_from_the_profile_not_the_global_key()
    {
        var (db, build, _, adminId) = await SeedL1Async(new EmptySettingsStore(new Dictionary<string, string>
        {
            [SettingsKeys.RecoUseImplicit] = "false",
        }));
        var other = new Profile
        {
            Name = "Other",
            Color = "teal",
            UseImplicit = false,
            CreatedAt = DateTime.UtcNow,
        };
        db.Profiles.Add(other);
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        db.Favorites.AddRange(
            new Favorite { ProfileId = adminId, TitleId = 101, CreatedAt = now },
            new Favorite { ProfileId = other.Id, TitleId = 101, CreatedAt = now });
        await db.SaveChangesAsync();

        await build.BuildAsync(adminId);
        await build.BuildAsync(other.Id);

        (await db.RecoStates.SingleAsync(s => s.ProfileId == adminId)).EffectiveN.ShouldBe(0.5);
        (await db.RecoStates.SingleAsync(s => s.ProfileId == other.Id)).EffectiveN.ShouldBe(0);
    }

    [Fact]
    public async Task Build_attributes_arr_add_and_download_only_to_that_profile()
    {
        var (db, build, _, adminId) = await SeedL1Async();
        var other = new Profile { Name = "Other", Color = "teal", CreatedAt = DateTime.UtcNow };
        db.Profiles.Add(other);
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        db.ArrAuditLogs.AddRange(
            new ArrAuditLog
            {
                ProfileId = null,
                At = now,
                Action = ArrAuditActions.Add,
                Kind = ArrKinds.Radarr,
                TitleId = 101,
                Options = "{}",
                Outcome = ArrAuditOutcomes.Success,
            },
            new ArrAuditLog
            {
                ProfileId = adminId,
                At = now,
                Action = ArrAuditActions.Add,
                Kind = ArrKinds.Radarr,
                TitleId = 102,
                Options = "{}",
                Outcome = ArrAuditOutcomes.Success,
            },
            new ArrAuditLog
            {
                ProfileId = adminId,
                At = now.AddMinutes(1),
                Action = ArrAuditActions.Delete,
                Kind = ArrKinds.Radarr,
                TitleId = 102,
                Options = "{}",
                Outcome = ArrAuditOutcomes.Success,
            },
            new ArrAuditLog
            {
                ProfileId = other.Id,
                At = now,
                Action = ArrAuditActions.Add,
                Kind = ArrKinds.Radarr,
                TitleId = 103,
                Options = "{}",
                Outcome = ArrAuditOutcomes.Success,
            });
        db.ArrLibraryItems.AddRange(
            new ArrLibraryItem
            {
                Kind = ArrKinds.Radarr,
                ArrId = 102,
                TitleId = 102,
                ArrTitle = "Other SciFi",
                State = ArrStates.Downloaded,
                SyncedAt = now,
            },
            new ArrLibraryItem
            {
                Kind = ArrKinds.Radarr,
                ArrId = 103,
                TitleId = 103,
                ArrTitle = "Drama Hit",
                State = ArrStates.Downloaded,
                SyncedAt = now,
            });
        await db.SaveChangesAsync();

        await build.BuildAsync(adminId);
        await build.BuildAsync(other.Id);

        var adminState = await db.RecoStates.SingleAsync(s => s.ProfileId == adminId);
        var otherState = await db.RecoStates.SingleAsync(s => s.ProfileId == other.Id);
        adminState.EffectiveN.ShouldBe(1);
        otherState.EffectiveN.ShouldBe(1);
    }

    [Fact]
    public async Task Historical_download_keeps_its_weight_after_deletion_and_explicit_rating_still_wins()
    {
        var (db, build, _, profileId) = await SeedL1Async();
        var now = DateTime.UtcNow;
        db.ArrAuditLogs.AddRange(new[] { ArrAuditActions.Add, ArrAuditActions.Download, ArrAuditActions.Delete }
            .Select(action => new ArrAuditLog
            {
                ProfileId = profileId,
                TitleId = 101,
                Kind = ArrKinds.Radarr,
                At = now,
                Action = action,
                Outcome = ArrAuditOutcomes.Success,
                Options = "{}",
            }));
        await db.SaveChangesAsync();

        await build.BuildAsync(profileId);
        (await db.RecoStates.SingleAsync(s => s.ProfileId == profileId)).EffectiveN.ShouldBe(1);
        (await db.ArrLibraryItems.CountAsync()).ShouldBe(0);

        db.Ratings.Add(new Rating { ProfileId = profileId, TitleId = 101, Value = RatingValues.Dislike, RatedAt = now });
        await db.SaveChangesAsync();
        await build.BuildAsync(profileId);
        var state = await db.RecoStates.SingleAsync(s => s.ProfileId == profileId);
        state.EffectiveN.ShouldBe(1);
        JsonSerializer.Deserialize<Dictionary<string, double>>(state.ProfileJson!)!.Values.ShouldAllBe(weight => weight < 0);
    }

    [Fact]
    public async Task External_genre_vote_reorders_only_pool_candidates_for_its_profile()
    {
        var (db, build, sourceId, profileId) = await SeedL1Async();
        var disabled = new CatalogSource
        {
            ProviderId = (await db.CatalogSources.SingleAsync(s => s.Id == sourceId)).ProviderId,
            CountryCode = "US",
            MetadataLanguage = "en-US",
            IsEnabled = false,
        };
        var other = new Profile { Name = "Other", Color = "teal", CreatedAt = DateTime.UtcNow };
        db.Profiles.Add(other);
        db.CatalogSources.Add(disabled);
        db.Titles.Add(Title(999, vote: 7.0, votes: 100, pop: 10));
        db.TitleGenres.Add(new TitleGenre { TitleId = 999, GenreId = 18 });
        db.Availabilities.Add(new Availability
        {
            TitleId = 999,
            CatalogSource = disabled,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await build.BuildAsync(profileId);
        var baseline = await db.Recommendations.AsNoTracking().Where(r => r.ProfileId == profileId)
            .OrderBy(r => r.Rank).ToListAsync();
        baseline.Select(r => r.TitleId).ShouldBe([101, 102, 103]);
        db.Ratings.Add(new Rating
        {
            ProfileId = profileId,
            TitleId = 999,
            Value = RatingValues.Love,
            RatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await build.BuildAsync(profileId);
        await build.BuildAsync(other.Id);

        var state = await db.RecoStates.SingleAsync(s => s.ProfileId == profileId);
        state.EffectiveN.ShouldBe(1.5);
        // Only one of the three candidates has genre 18. Counting the external drama in DF or N
        // changes its weight relative to the movie term, which occurs on all three.
        var genreIdf = 1 + Math.Log(4.0 / 2);
        var genreWeight = genreIdf / Math.Sqrt(genreIdf * genreIdf + 0.5 * 0.5);
        JsonSerializer.Deserialize<Dictionary<string, double>>(state.ProfileJson!)![RecoSparse.GenreTerm(18)]
            .ShouldBe(genreWeight, 1e-6);
        (await db.RecoStates.SingleAsync(s => s.ProfileId == other.Id)).EffectiveN.ShouldBe(0);
        var rows = await db.Recommendations
            .Where(r => r.ProfileId == profileId && r.Version == state.CurrentVersion)
            .OrderBy(r => r.Rank).ToListAsync();
        rows.Select(r => r.TitleId).ShouldBe([103, 101, 102]);
        rows[0].Personal.ShouldBe(1.0);
        rows[0].ReasonsJson.ShouldContain("T999");
        rows[0].Prior.ShouldBe(baseline[2].Prior);
        rows[0].Score.ShouldBe(RecoLearning.FuseScore(rows[0].Prior, 1, RecoLearning.PersonalWeight(1.5)), 1e-9);
    }

    [Fact]
    public async Task Build_mixes_only_usable_pool_signals_with_missing_metadata_signals()
    {
        var (db, build, _, profileId) = await SeedL1Async();
        var external = Title(998, vote: 7.0, votes: 100, pop: 10);
        external.Category = TitleCategories.Series; // no term in the movie-only pool vocabulary
        db.Titles.Add(external);
        db.Ratings.Add(new Rating
        {
            ProfileId = profileId,
            TitleId = 101,
            Value = RatingValues.Like,
            RatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await build.BuildAsync(profileId);
        var baseline = await db.Recommendations.OrderBy(r => r.Rank)
            .Select(r => new { r.TitleId, r.Score }).ToListAsync();

        db.Ratings.Add(new Rating
        {
            ProfileId = profileId,
            TitleId = 998,
            Value = RatingValues.Love,
            RatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await build.BuildAsync(profileId);
        var state = await db.RecoStates.SingleAsync(s => s.ProfileId == profileId);
        state.EffectiveN.ShouldBe(1);
        var actual = await db.Recommendations.OrderBy(r => r.Rank)
            .Select(r => new { r.TitleId, r.Score }).ToListAsync();
        actual.ShouldBe(baseline);
    }

    [Fact]
    public async Task External_embedding_only_vote_needs_active_compatible_pool_embeddings()
    {
        var (db, build, _, profileId) = await SeedL1Async();
        var external = Title(997, vote: 7, votes: 100, pop: 10);
        external.Category = TitleCategories.Series;
        db.Titles.Add(external);
        db.Favorites.Add(new Favorite { ProfileId = profileId, TitleId = 997, CreatedAt = DateTime.UtcNow });
        SeedUnitEmbedding(db, 101, [0f, 1f]);
        SeedUnitEmbedding(db, 102, [0f, 1f]);
        SeedUnitEmbedding(db, 103, [1f, 0f]);
        SeedUnitEmbedding(db, 997, [1f, 0f, 0f]);
        var state = new RecoState
        {
            ProfileId = profileId,
            ModelStatus = RecoModelStatuses.Ready,
            ModelId = RecoModelCatalog.DefaultModelId,
            EmbeddingCoverage = 1,
        };
        db.RecoStates.Add(state);
        await db.SaveChangesAsync();
        await build.BuildAsync(profileId);
        state.EffectiveN.ShouldBe(0); // wrong dimension

        (await db.TitleEmbeddings.SingleAsync(e => e.TitleId == 997)).Vector =
            RecoEmbeddingService.FloatsToBytes([1f, 0f]);
        state.EmbeddingCoverage = 0;
        await db.SaveChangesAsync();
        await build.BuildAsync(profileId);
        state.EffectiveN.ShouldBe(0); // model gated off

        state.EmbeddingCoverage = 1;
        await db.SaveChangesAsync();
        await build.BuildAsync(profileId);
        state.EffectiveN.ShouldBe(0.5);
        state.ProfileJson.ShouldBeNull(); // no sparse evidence
        var rows = await db.Recommendations.OrderBy(r => r.Rank).ToListAsync();
        rows.Select(r => r.TitleId).ShouldBe([103, 101, 102]);
        rows[0].Personal.ShouldNotBeNull();
        rows[0].Score.ShouldBeGreaterThan(rows[1].Score);
    }

    private static void SeedUnitEmbedding(PickarrDbContext db, int titleId, float[] vector)
    {
        RecoKnn.L2Normalize(vector);
        db.TitleEmbeddings.Add(new TitleEmbedding
        {
            TitleId = titleId,
            ModelId = RecoModelCatalog.DefaultModelId,
            Vector = RecoEmbeddingService.FloatsToBytes(vector),
            TextHash = $"hash-{titleId}",
            UpdatedAt = DateTime.UtcNow,
        });
    }

    private async Task<(PickarrDbContext Db, RecoBuildService Build)> SeedAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<PickarrDbContext>()
            .UseSqlite(_connection)
            .Options;
        var db = new PickarrDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var enabledProvider = new Provider
        {
            Key = "netflix",
            DisplayName = "Netflix",
            ProviderType = "streaming",
            IsEnabled = true,
            CreatedAt = now,
        };
        var disabledProvider = new Provider
        {
            Key = "prime",
            DisplayName = "Prime",
            ProviderType = "streaming",
            IsEnabled = false,
            CreatedAt = now,
        };
        db.Providers.AddRange(enabledProvider, disabledProvider);
        await db.SaveChangesAsync();

        var enabledSource = new CatalogSource
        {
            ProviderId = enabledProvider.Id,
            CountryCode = "IT",
            MetadataLanguage = "it-IT",
            IsEnabled = true,
        };
        var disabledSource = new CatalogSource
        {
            ProviderId = enabledProvider.Id,
            CountryCode = "US",
            MetadataLanguage = "en-US",
            IsEnabled = false,
        };
        var providerDisabledSource = new CatalogSource
        {
            ProviderId = disabledProvider.Id,
            CountryCode = "IT",
            MetadataLanguage = "it-IT",
            IsEnabled = true,
        };
        db.CatalogSources.AddRange(enabledSource, disabledSource, providerDisabledSource);
        await db.SaveChangesAsync();

        var t10 = Title(10, vote: 6.0, votes: 100, pop: 5);
        var t20 = Title(20, vote: 9.0, votes: 5000, pop: 80);
        var t30 = Title(30, vote: 9.5, votes: 9000, pop: 90);
        var t40 = Title(40, vote: 9.9, votes: 9000, pop: 99);
        db.Titles.AddRange(t10, t20, t30, t40);

        db.Availabilities.AddRange(
            new Availability
            {
                TitleId = 10,
                CatalogSourceId = enabledSource.Id,
                FirstSeenAt = now,
                LastSeenAt = now,
                RemovedAt = now, // still in pool
            },
            new Availability
            {
                TitleId = 20,
                CatalogSourceId = enabledSource.Id,
                FirstSeenAt = now,
                LastSeenAt = now,
            },
            new Availability
            {
                TitleId = 30,
                CatalogSourceId = disabledSource.Id,
                FirstSeenAt = now,
                LastSeenAt = now,
            },
            new Availability
            {
                TitleId = 30,
                CatalogSourceId = providerDisabledSource.Id,
                FirstSeenAt = now,
                LastSeenAt = now,
            });
        // title 40: no availability

        var admin = new Profile { Name = "Admin", Color = Profile.DefaultColor, IsAdmin = true, CreatedAt = now };
        db.Profiles.Add(admin);
        await db.SaveChangesAsync();

        var build = new RecoBuildService(db, new FixedClock(now), new EmptySettingsStore(), new RecoLevel3Model());
        return (db, build);
    }

    private async Task<(PickarrDbContext Db, RecoBuildService Build, int EnabledSourceId, int AdminId)> SeedL1Async(
        ISettingsStore? settings = null,
        IRecoLevel3Model? level3Model = null,
        bool jsonCollections = false)
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<PickarrDbContext>()
            .UseSqlite(_connection, sqlite =>
            {
                if (jsonCollections) sqlite.UseParameterizedCollectionMode(ParameterTranslationMode.Parameter);
            })
            .Options;
        var db = new PickarrDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var provider = new Provider
        {
            Key = "netflix",
            DisplayName = "Netflix",
            ProviderType = "streaming",
            IsEnabled = true,
            CreatedAt = now,
        };
        db.Providers.Add(provider);
        await db.SaveChangesAsync();

        var source = new CatalogSource
        {
            ProviderId = provider.Id,
            CountryCode = "IT",
            MetadataLanguage = "en-US",
            IsEnabled = true,
        };
        db.CatalogSources.Add(source);
        await db.SaveChangesAsync();

        db.Genres.AddRange(
            new Genre { Id = 878 },
            new Genre { Id = 18 });
        db.GenreLocalizations.AddRange(
            new GenreLocalization { GenreId = 878, Language = "en-US", Name = "Science Fiction" },
            new GenreLocalization { GenreId = 18, Language = "en-US", Name = "Drama" });

        // Equal priors so sparse L1 decides the order (cold-start w is still small).
        var t101 = Title(101, vote: 7.0, votes: 200, pop: 20);
        t101.OriginalTitle = "Liked SciFi";
        var t102 = Title(102, vote: 7.0, votes: 200, pop: 20);
        t102.OriginalTitle = "Other SciFi";
        var t103 = Title(103, vote: 7.0, votes: 200, pop: 20);
        t103.OriginalTitle = "Drama Hit";
        db.Titles.AddRange(t101, t102, t103);
        db.TitleGenres.AddRange(
            new TitleGenre { TitleId = 101, GenreId = 878 },
            new TitleGenre { TitleId = 102, GenreId = 878 },
            new TitleGenre { TitleId = 103, GenreId = 18 });
        db.TitleLocalizations.AddRange(
            new TitleLocalization { TitleId = 101, Language = "en-US", Name = "Liked SciFi", NameNormalized = "liked scifi" },
            new TitleLocalization { TitleId = 102, Language = "en-US", Name = "Other SciFi", NameNormalized = "other scifi" },
            new TitleLocalization { TitleId = 103, Language = "en-US", Name = "Drama Hit", NameNormalized = "drama hit" });

        db.Availabilities.AddRange(
            new Availability { TitleId = 101, CatalogSourceId = source.Id, FirstSeenAt = now, LastSeenAt = now },
            new Availability { TitleId = 102, CatalogSourceId = source.Id, FirstSeenAt = now, LastSeenAt = now },
            new Availability { TitleId = 103, CatalogSourceId = source.Id, FirstSeenAt = now, LastSeenAt = now });
        var admin = new Profile { Name = "Admin", Color = Profile.DefaultColor, IsAdmin = true, CreatedAt = now };
        db.Profiles.Add(admin);
        await db.SaveChangesAsync();

        var build = new RecoBuildService(
            db,
            new FixedClock(now),
            settings ?? new EmptySettingsStore(),
            level3Model ?? new RecoLevel3Model());
        return (db, build, source.Id, admin.Id);
    }

    private static Title Title(int id, double vote, int votes, double pop) => new()
    {
        Id = id,
        MediaType = MediaTypes.Movie,
        Category = TitleCategories.Movie,
        OriginalTitle = $"T{id}",
        VoteTmdb = vote,
        VoteTmdbCount = votes,
        Popularity = pop,
        MetadataStatus = MetadataStatuses.Ready,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class CapturingLevel3Model : IRecoLevel3Model
    {
        public List<List<RecoLevel3Example>> Calls { get; } = [];

        public IRecoLevel3Scorer? TryTrain(
            IReadOnlyList<RecoLevel3Example> examples,
            int minRatings,
            int minPerClass)
        {
            Calls.Add(examples.Select(example => example with { Features = example.Features.ToArray() }).ToList());
            return null;
        }
    }

    private sealed class EmptySettingsStore(Dictionary<string, string>? values = null) : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = values ?? [];

        public Task<string?> GetRawAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, string valueJson, bool isSecret, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> IsSetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
