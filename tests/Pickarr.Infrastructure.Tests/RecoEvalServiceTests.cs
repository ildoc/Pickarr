using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Domain;
using Pickarr.Core.Domain.Entities;
using Pickarr.Core.Metadata;
using Pickarr.Core.Recommendations;
using Pickarr.Core.Settings;
using Pickarr.Infrastructure.Catalog;
using Pickarr.Infrastructure.Metadata.Reco;
using Pickarr.Infrastructure.Persistence;
using Shouldly;

namespace Pickarr.Infrastructure.Tests;

public sealed class RecoEvalServiceTests : IAsyncLifetime
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

    [Fact]
    public async Task Evaluate_with_few_ratings_sets_low_sample_warning_and_keeps_l3_inactive()
    {
        var (db, eval) = await SeedAsync(ratingCount: 12, minRatings: 100);
        var report = await eval.EvaluateAsync();

        report.LowSampleWarning.ShouldBeTrue();
        report.Level3Active.ShouldBeFalse();
        report.Prior.ShouldNotBeNull();
        report.Level1.ShouldNotBeNull();
        report.Combined.ShouldNotBeNull();

        var state = await db.RecoStates.SingleAsync(s => s.Id == RecoState.SingletonId);
        state.LastEvalJson.ShouldNotBeNullOrWhiteSpace();
        state.Level3Active.ShouldBeFalse();

        var latest = await eval.GetLatestAsync();
        latest.ShouldNotBeNull();
        latest!.RatingCount.ShouldBe(12);
    }

    [Fact]
    public async Task Level3_inactive_under_min_ratings_even_when_gate_checked()
    {
        var (_, eval) = await SeedAsync(ratingCount: 40, minRatings: 100);
        var report = await eval.EvaluateAsync();
        report.Level3Active.ShouldBeFalse();
        report.Level3SkipReason.ShouldBe("below_min_ratings");
    }

    [Fact]
    public async Task Retrieval_folds_only_use_positive_ratings_and_exclude_all_rated_from_unrated()
    {
        var (db, eval) = await SeedAsync(ratingCount: 4, minRatings: 100);
        var titles = await db.Titles.OrderBy(t => t.Id).ToListAsync();
        var ratings = await db.Ratings.OrderBy(r => r.TitleId).ToListAsync();
        ratings[0].Value = RatingValues.Love;
        ratings[1].Value = RatingValues.Dislike;
        ratings[2].Value = RatingValues.NotInterested;
        ratings[3].Value = RatingValues.Like;
        foreach (var title in titles)
        {
            var positive = title.Id == ratings[0].TitleId || title.Id == ratings[3].TitleId;
            var explicitlyNegative = title.Id == ratings[1].TitleId || title.Id == ratings[2].TitleId;
            title.VoteTmdb = explicitlyNegative ? 10 : positive ? 5 : 1;
            title.VoteTmdbCount = 1000;
            title.Popularity = explicitlyNegative ? 100 : positive ? 50 : 1;
            title.Category = TitleCategories.Movie;
            title.ReleaseYear = 2000;
        }

        await db.SaveChangesAsync();
        var report = await eval.EvaluateAsync();

        report.RatingCount.ShouldBe(4);
        report.LikeCount.ShouldBe(2);
        report.DislikeCount.ShouldBe(1);
        report.NotInterestedCount.ShouldBe(1);
        report.UnratedPoolCount.ShouldBe(76);
        report.UnratedSamplePerRetrievalFold.ShouldBe(40);
        report.EvaluatedPositiveCount.ShouldBe(2);
        report.EvaluatedDislikeCount.ShouldBe(1);
        report.EvaluatedNotInterestedCount.ShouldBe(1);
        report.Prior.ShouldNotBeNull();
        report.Prior.Folds.ShouldBe(2);
        report.Prior.Auc.ShouldBe(1);
        report.LikeVsDislikeAucCombined.ShouldBe(0);
        report.LikeVsNotInterestedAucCombined.ShouldBe(0);
        var latest = await eval.GetLatestAsync();
        latest.ShouldNotBeNull();
        latest.NotInterestedCount.ShouldBe(1);
        latest.UnratedSamplePerRetrievalFold.ShouldBe(40);
    }

    [Fact]
    public async Task Retrieval_is_unavailable_without_unrated_candidates()
    {
        var (_, eval) = await SeedAsync(ratingCount: 80, minRatings: 40);
        var report = await eval.EvaluateAsync();

        report.Prior.ShouldBeNull();
        report.Combined.ShouldBeNull();
        report.UnratedPoolCount.ShouldBe(0);
        report.UnratedSamplePerRetrievalFold.ShouldBe(0);
        report.EvaluatedPositiveCount.ShouldBe(50);
        report.EvaluatedDislikeCount.ShouldBe(27);
        report.Level3Active.ShouldBeFalse();
        report.Level3SkipReason.ShouldBe("insufficient_retrieval_data");
    }

    [Fact]
    public async Task Evaluation_reports_missing_classes_without_inventing_auc()
    {
        var (db, eval) = await SeedAsync(ratingCount: 2, minRatings: 100);
        var ratings = await db.Ratings.OrderBy(r => r.TitleId).ToListAsync();
        ratings[0].Value = RatingValues.Dislike;
        ratings[1].Value = RatingValues.NotInterested;
        await db.SaveChangesAsync();

        var report = await eval.EvaluateAsync();
        report.Prior.ShouldBeNull();
        report.LikeVsDislikeAucCombined.ShouldBeNull();
        report.LikeVsNotInterestedAucCombined.ShouldBeNull();
        report.EvaluatedPositiveCount.ShouldBe(0);
        report.EvaluatedDislikeCount.ShouldBe(1);
        report.EvaluatedNotInterestedCount.ShouldBe(1);
        report.Level3Active.ShouldBeFalse();
    }

    [Fact]
    public async Task Evaluation_with_only_positives_reports_actual_small_sample_and_missing_dislikes()
    {
        var (db, eval) = await SeedAsync(ratingCount: 79, minRatings: 100);
        foreach (var rating in await db.Ratings.ToListAsync())
        {
            rating.Value = RatingValues.Like;
        }

        await db.SaveChangesAsync();
        var report = await eval.EvaluateAsync();
        report.Prior.ShouldNotBeNull();
        report.Prior.Folds.ShouldBe(50);
        report.UnratedPoolCount.ShouldBe(1);
        report.UnratedSamplePerRetrievalFold.ShouldBe(1);
        report.EvaluatedPositiveCount.ShouldBe(50);
        report.EvaluatedDislikeCount.ShouldBe(0);
        report.LikeVsDislikeAucCombined.ShouldBeNull();
    }

    [Fact]
    public async Task Evaluation_without_ratings_reports_zero_counts_and_no_metrics()
    {
        var (_, eval) = await SeedAsync(ratingCount: 0, minRatings: 100);
        var report = await eval.EvaluateAsync();
        report.UnratedPoolCount.ShouldBe(80);
        report.UnratedSamplePerRetrievalFold.ShouldBe(0);
        report.NotInterestedCount.ShouldBe(0);
        report.EvaluatedPositiveCount.ShouldBe(0);
        report.Prior.ShouldBeNull();
        report.LikeVsDislikeAucCombined.ShouldBeNull();
    }

    [Fact]
    public async Task Level3_training_features_do_not_use_their_own_rating_target()
    {
        var firstModel = new CapturingLevel3Model();
        var (firstDb, firstEval) = await SeedAsync(60, 40, firstModel);
        var targetId = await firstDb.Ratings.OrderBy(r => r.TitleId).Select(r => r.TitleId).FirstAsync();
        await firstEval.EvaluateAsync();
        var firstFeatures = firstModel.Calls
            .SelectMany(call => call)
            .First(example => example.TitleId == targetId)
            .Features;

        await firstDb.DisposeAsync();
        await _connection!.DisposeAsync();
        _connection = null;

        var secondModel = new CapturingLevel3Model();
        var (secondDb, secondEval) = await SeedAsync(60, 40, secondModel);
        var targetRating = await secondDb.Ratings.SingleAsync(r => r.TitleId == targetId);
        targetRating.Value = RatingValues.Like;
        await secondDb.SaveChangesAsync();
        await secondEval.EvaluateAsync();
        var secondFeatures = secondModel.Calls
            .SelectMany(call => call)
            .First(example => example.TitleId == targetId)
            .Features;

        secondFeatures.ShouldBe(firstFeatures);
    }

    [Fact]
    public async Task Level3_gate_requires_beating_the_combined_score_not_only_level2()
    {
        var (db, eval) = await SeedAsync(40, 20, new YearLevel3Model(), embeddingsEnabled: true);
        var titles = await db.Titles.OrderBy(title => title.Id).ToListAsync();
        var ratings = await db.Ratings.OrderBy(rating => rating.TitleId).ToListAsync();
        for (var i = 0; i < ratings.Count; i++)
        {
            ratings[i].Value = i % 2 == 0 ? RatingValues.Like : RatingValues.Dislike;
        }

        foreach (var title in titles)
        {
            title.ReleaseYear = 1950;
            title.VoteTmdb = 7;
            title.VoteTmdbCount = 1000;
            title.Popularity = 10;
        }

        var likes = ratings.Where(rating => RatingValues.IsPositive(rating.Value)).ToList();
        foreach (var (rating, index) in likes.Select((rating, index) => (rating, index)))
        {
            titles.Single(title => title.Id == rating.TitleId).ReleaseYear = index < 12 ? 2015 : 1950;
        }

        db.TitleGenres.RemoveRange(await db.TitleGenres.ToListAsync());
        db.TitleGenres.AddRange(likes.Select(rating => new TitleGenre { TitleId = rating.TitleId, GenreId = 1 }));
        db.TitleGenres.AddRange(ratings.Where(rating => !RatingValues.IsPositive(rating.Value))
            .Select(rating => new TitleGenre { TitleId = rating.TitleId, GenreId = 2 }));
        var ratedIds = ratings.Select(rating => rating.TitleId).ToHashSet();
        db.TitleGenres.AddRange(titles.Where(title => !ratedIds.Contains(title.Id))
            .Select(title => new TitleGenre { TitleId = title.Id, GenreId = 3 }));
        SeedUnitEmbedding(db, ratings[0].TitleId, [1f, 0f]);
        // Keep a training neighbour available in every held-out fold, without a kNN advantage.
        SeedUnitEmbedding(db, ratings[1].TitleId, [0f, 1f]);
        var state = await db.RecoStates.SingleAsync();
        state.ModelStatus = RecoModelStatuses.Ready;
        state.ModelId = RecoModelCatalog.DefaultModelId;
        state.EmbeddingCoverage = 1;
        await db.SaveChangesAsync();

        var report = await eval.EvaluateAsync();

        report.Level2.ShouldNotBeNull();
        report.Combined.ShouldNotBeNull();
        report.Level3.ShouldNotBeNull();
        report.Level2.Auc.ShouldBe(0.5);
        report.Level3.Auc.ShouldBe(0.8);
        report.Combined.Auc.ShouldBe(1.0);
        report.Level2.Auc.ShouldBeLessThan(report.Level3.Auc);
        report.Combined.Auc.ShouldBeGreaterThan(report.Level3.Auc);
        report.Level2.Folds.ShouldBe(report.Combined.Folds);
        report.Level3.Folds.ShouldBe(report.Combined.Folds);
        report.Level3Active.ShouldBeFalse();
        report.Level3SkipReason.ShouldBe("did_not_beat_combined");
    }

    [Fact]
    public async Task Latest_legacy_report_deserializes_without_new_counts()
    {
        var (db, eval) = await SeedAsync(ratingCount: 0, minRatings: 100);
        var state = await db.RecoStates.SingleAsync();
        state.LastEvalJson = """{"evaluatedAt":"2026-01-01T00:00:00Z","ratingCount":1,"likeCount":1,"dislikeCount":0,"poolSize":80,"lowSampleWarning":true,"level3Active":false}""";
        await db.SaveChangesAsync();

        var report = await eval.GetLatestAsync();
        report.ShouldNotBeNull();
        report.RatingCount.ShouldBe(1);
        report.NotInterestedCount.ShouldBeNull();
        report.EvaluatedPositiveCount.ShouldBeNull();
    }

    private async Task<(PickarrDbContext Db, RecoEvalService Eval)> SeedAsync(
        int ratingCount,
        int minRatings,
        IRecoLevel3Model? level3Model = null,
        bool embeddingsEnabled = false)
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PickarrDbContext>().UseSqlite(_connection).Options;
        var db = new PickarrDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var now = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
        var provider = new Provider
        {
            Key = "netflix",
            DisplayName = "Netflix",
            ProviderType = "watchmode",
            ExternalId = "203",
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
            BaselineCompletedAt = now,
        };
        db.CatalogSources.Add(source);
        await db.SaveChangesAsync();

        for (var i = 0; i < 80; i++)
        {
            db.Titles.Add(new Title
            {
                WatchmodeId = 9_000_000 + i,
                MediaType = i % 2 == 0 ? MediaTypes.Movie : MediaTypes.Series,
                Category = i % 2 == 0 ? TitleCategories.Movie : TitleCategories.Series,
                OriginalTitle = $"Eval {i}",
                OriginalTitleNormalized = $"eval {i}",
                ReleaseYear = 2000 + (i % 20),
                VoteTmdb = 5 + (i % 40) / 10.0,
                VoteTmdbCount = 100 + i,
                Popularity = 1 + i,
                MetadataStatus = MetadataStatuses.Ready,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        for (var g = 1; g <= 5; g++)
        {
            db.Genres.Add(new Genre { Id = g });
        }

        await db.SaveChangesAsync();
        var titles = await db.Titles.OrderBy(t => t.Id).ToListAsync();
        foreach (var t in titles)
        {
            db.Availabilities.Add(new Availability
            {
                TitleId = t.Id,
                CatalogSourceId = source.Id,
                FirstSeenAt = now,
                LastSeenAt = now,
                AddedAt = now,
            });
            db.TitleGenres.Add(new TitleGenre { TitleId = t.Id, GenreId = (t.Id % 5) + 1 });
        }

        var profile = new Profile { Name = "Eval", Color = Profile.DefaultColor, IsAdmin = true, CreatedAt = now };
        db.Profiles.Add(profile);
        for (var i = 0; i < ratingCount; i++)
        {
            db.Ratings.Add(new Rating
            {
                Profile = profile,
                TitleId = titles[i].Id,
                Value = i % 3 == 0 ? RatingValues.Dislike : RatingValues.Like,
                RatedAt = now.AddMinutes(-i),
            });
        }

        await db.SaveChangesAsync();
        db.RecoStates.Add(new RecoState { Id = RecoState.SingletonId, ProfileId = profile.Id });
        await db.SaveChangesAsync();

        var settings = new MemorySettingsStore();
        await settings.SetAsync(SettingsKeys.RecoMlMinRatings, minRatings.ToString(), false);
        await settings.SetAsync(SettingsKeys.RecoEmbeddingsEnabled, embeddingsEnabled.ToString(), false);
        await settings.SetAsync(SettingsKeys.RecoN0, "30", false);

        var eval = new RecoEvalService(db, new FixedClock(now), settings, level3Model ?? new RecoLevel3Model());
        return (db, eval);
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
            return new FixedScorer();
        }

        private sealed class FixedScorer : IRecoLevel3Scorer
        {
            public double Score(float[] features) => 0.5;
        }
    }

    private sealed class YearLevel3Model : IRecoLevel3Model
    {
        public IRecoLevel3Scorer? TryTrain(
            IReadOnlyList<RecoLevel3Example> examples,
            int minRatings,
            int minPerClass) =>
            examples.Count >= minRatings
                ? new YearScorer()
                : null;

        private sealed class YearScorer : IRecoLevel3Scorer
        {
            public double Score(float[] features) => features[RecoLevel3.YearFeatureIndex];
        }
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

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class MemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<string?> GetRawAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);

        public Task SetAsync(string key, string valueJson, bool isSecret, CancellationToken cancellationToken = default)
        {
            _values[key] = valueJson;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> IsSetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.ContainsKey(key));
    }
}
