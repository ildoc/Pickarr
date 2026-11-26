using Pickarr.Core.Abstractions;
using Pickarr.Core.Recommendations;
using Pickarr.Infrastructure.Metadata.Reco;
using Pickarr.RecoBench;
using Microsoft.Data.Sqlite;
using Shouldly;

namespace Pickarr.Infrastructure.Tests;

public sealed class RecoTemporalRankingTests
{
    private static readonly DateTime Epoch = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static DateTime Day(int n) => Epoch.AddDays(n);
    private static TemporalRatingEvent E(long id, int profile, int title, int day, string? value) =>
        new(id, profile, title, Day(day), value);

    private static BenchmarkTitle Title(int id, int genre, float[]? vector = null, double? popularity = 1) =>
        new(new RecoSparseTitleFeatures(id, "", null, null, [genre], [], [], [], []),
            new RecoTitleStats(id, 5, popularity), vector is null ? null : "model-v1", vector);

    private static BenchmarkCatalog Catalog() => new(
        [Title(1, 1, [1, 0]), Title(2, 2, [0, 1]), Title(3, 1, [1, 0]),
         Title(4, 2, [0, 1]), Title(5, 3, [0.6f, 0.8f]), Title(6, 1, [1, 0])],
        [new(1, 1), new(2, 1), new(3, 1), new(4, 1), new(5, 1), new(6, 2)],
        [new(1, true, true), new(2, false, true)], "model-v1", 2);

    [Fact]
    public void Full_pool_excludes_previously_rated_and_ambiguous_but_not_deleted_and_orders_ties()
    {
        var events = new[]
        {
            E(1, 1, 1, 0, "Love"), E(2, 1, 2, 0, "Dislike"),
            E(3, 1, 4, 0, "Like"), E(4, 1, 4, 1, null),
            E(5, 1, 5, 1, "Like"), E(6, 1, 5, 1, "Dislike")
        };
        var result = TemporalRanking.Score(Catalog(), events, 1, Day(2), Day(2));
        result.PoolCount.ShouldBe(5);
        result.EligibleCount.ShouldBe(2);
        result.Prior.Titles.Select(x => x.TitleId).ShouldBe(new[] { 3, 4 });
        result.Sparse.Titles.Select(x => x.TitleId).ShouldBe(new[] { 3, 4 });
        result.Sparse.Titles[0].Score.ShouldBeGreaterThan(0);
        result.Sparse.Titles[1].Score.ShouldBeLessThan(0);
        result.Embedding.Titles.Select(x => x.TitleId).ShouldBe(new[] { 3, 4 });
        result.Embedding.Titles[0].Score.ShouldBe(0.575, 1e-6);
        result.Embedding.Titles[1].Score.ShouldBe(0.45, 1e-6);
        result.Combined.Titles.Count.ShouldBe(2);
        result.Reranked.Titles.Select(x => x.TitleId).Order().ShouldBe(new[] { 3, 4 });
        result.Level3.UnavailableReason.ShouldBe("level3_trainer_unavailable");
    }

    [Fact]
    public void Ranking_and_mmr_keep_the_entire_pool_beyond_the_mmr_prefix()
    {
        var titles = Enumerable.Range(1, 150).Select(id => Title(id, 1, popularity: id)).ToArray();
        var catalog = new BenchmarkCatalog(titles, titles.Select(t => new RecoAvailabilityFact(t.Features.TitleId, 1)).ToArray(),
            [new(1, true, true)], "model-v1", 2);
        var result = TemporalRanking.Score(catalog, [], 1, Day(1), Day(1));
        result.Prior.Titles.Count.ShouldBe(150);
        result.Prior.Titles[0].TitleId.ShouldBe(150);
        result.Combined.Titles.Count.ShouldBe(150);
        result.Reranked.Titles.Count.ShouldBe(150);
        result.Reranked.Titles.Select(x => x.TitleId).Distinct().Count().ShouldBe(150);
    }

    [Fact]
    public void Frozen_cutoffs_keep_training_features_and_validation_ranking_independent_of_future_labels()
    {
        var titles = Enumerable.Range(1, 104).Select(id => Title(id, id % 3)).ToArray();
        var catalog = new BenchmarkCatalog(titles, titles.Select(t => new RecoAvailabilityFact(t.Features.TitleId, 1)).ToArray(),
            [new(1, true, true)], "model-v1", 2);
        var prefix = Enumerable.Range(1, 100)
            .Select(id => E(id, 1, id, id, id % 2 == 1 ? "Love" : "Dislike"))
            .Append(E(101, 2, 103, 0, "Like")).ToArray();
        var futureA = prefix.Concat([E(102, 1, 103, 102, "Love"), E(103, 2, 104, 102, "Like")]).ToArray();
        var futureB = prefix.Concat([E(102, 1, 103, 102, "Dislike"), E(103, 2, 104, 102, null)]).ToArray();
        var a = new CapturingModel();
        var b = new CapturingModel();
        var ra = TemporalRanking.Score(catalog, futureA, 1, Day(101), Day(102), a);
        var rb = TemporalRanking.Score(catalog, futureB, 1, Day(101), Day(102), b);
        ra.Combined.Titles.ShouldBe(rb.Combined.Titles);
        ra.Level3.Titles.ShouldBe(rb.Level3.Titles);
        a.Examples.Count.ShouldBe(100);
        a.Examples.Take(2).Select(x => x.IsLike).ShouldBe(new[] { true, false });
        for (var i = 0; i < a.Examples.Count; i++)
            a.Examples[i].Features.ShouldBe(b.Examples[i].Features);
        a.Examples[0].Features[RecoLevel3.PersonalWeightFeatureIndex].ShouldBe(0);
        a.Examples[1].Features[RecoLevel3.PersonalWeightFeatureIndex].ShouldBe(0.05f, 0.00001f);
        var targetChanged = futureA.Select(e => e.Id == 2 ? e with { Value = "Love" } : e).ToArray();
        var c = new CapturingModel();
        TemporalRanking.Score(catalog, targetChanged, 1, Day(101), Day(102), c);
        a.Examples[1].Features.ShouldBe(c.Examples[1].Features);
        a.Examples[1].IsLike.ShouldNotBe(c.Examples[1].IsLike);
    }

    [Fact]
    public void Level3_training_and_scoring_receive_the_usable_vote_count()
    {
        var titles = Enumerable.Range(1, 104)
            .Select(id => Title(id, id % 3) with { UsableVoteCount = id * 100 })
            .ToArray();
        var catalog = new BenchmarkCatalog(titles, titles.Select(t => new RecoAvailabilityFact(t.Features.TitleId, 1)).ToArray(),
            [new(1, true, true)], "model-v1", 2);
        var events = Enumerable.Range(1, 100)
            .Select(id => E(id, 1, id, id, id % 2 == 1 ? "Love" : "Dislike"))
            .ToArray();
        var model = new VoteCountModel();

        var result = TemporalRanking.Score(catalog, events, 1, Day(101), Day(102), model);

        model.Examples.Count.ShouldBe(100);
        foreach (var example in model.Examples)
            example.Features[RecoLevel3.VoteCountFeatureIndex]
                .ShouldBe((float)Math.Min(1, Math.Log10(1 + example.TitleId * 100) / 6), 1e-6f);
        result.Level3.Titles.Select(x => x.TitleId).ShouldBe(new[] { 104, 103, 102, 101 });
    }

    [Fact]
    public void Missing_or_incompatible_vectors_are_explicit_and_never_merged()
    {
        var original = Catalog();
        var incomplete = original with
        {
            Titles = original.Titles.Select(t => t.Features.TitleId == 4
            ? t with { Embedding = null, EmbeddingModelId = null } : t).ToArray()
        };
        var events = new[] { E(1, 1, 1, 0, "Like") };
        var result = TemporalRanking.Score(incomplete, events, 1, Day(1), Day(1));
        result.Embedding.UnavailableReason.ShouldBe("embedding_coverage_incomplete");
        result.Embedding.Titles.ShouldBeEmpty();
        result.Combined.Titles.Count.ShouldBe(4);
        result.Limitations.ShouldContain("combined_and_sparse_mmr_are_proxies_not_production_ranking");
        var invalid = original with
        {
            Titles = original.Titles.Select(t => t.Features.TitleId == 4
            ? t with { EmbeddingModelId = "other" } : t).ToArray()
        };
        Should.Throw<ArgumentException>(() => TemporalRanking.Score(invalid, events, 1, Day(1), Day(1)));
        invalid = original with
        {
            Titles = original.Titles.Select(t => t.Features.TitleId == 4
            ? t with { Embedding = [float.NaN, 0] } : t).ToArray()
        };
        Should.Throw<ArgumentException>(() => TemporalRanking.Score(invalid, events, 1, Day(1), Day(1)));
        var noSignals = TemporalRanking.Score(original, [], 1, Day(1), Day(1), new RejectingModel());
        noSignals.Sparse.UnavailableReason.ShouldBe("sparse_profile_unavailable");
        noSignals.Embedding.UnavailableReason.ShouldBe("embedding_signals_unavailable");
        noSignals.Level3.UnavailableReason.ShouldBe("level3_insufficient_training");
        var noSparse = original with
        {
            Titles = original.Titles.Select(t => t.Features.TitleId == 4
                ? t with { Features = t.Features with { GenreIds = [] } } : t).ToArray()
        };
        TemporalRanking.Score(noSparse, events, 1, Day(1), Day(1)).Sparse.UnavailableReason
            .ShouldBe("sparse_coverage_incomplete");
    }

    [Fact]
    public void Reads_current_catalog_query_only_including_removed_availability_and_only_selected_model()
    {
        using var db = new SqliteConnection("Data Source=:memory:");
        db.Open();
        using (var command = db.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE Titles (Id INTEGER, Category TEXT, OriginalLanguage TEXT, ReleaseYear INTEGER, OriginCountries TEXT, VoteTmdb REAL, VoteTmdbCount INTEGER, Popularity REAL, VoteImdb REAL, VoteImdbCount INTEGER, VoteBlended REAL);
                CREATE TABLE TitleGenres (TitleId INTEGER, GenreId INTEGER);
                CREATE TABLE TitleKeywords (TitleId INTEGER, KeywordId INTEGER);
                CREATE TABLE TitleCredits (TitleId INTEGER, PersonId INTEGER, Role TEXT);
                CREATE TABLE TitleEmbeddings (TitleId INTEGER, ModelId TEXT, Vector BLOB);
                CREATE TABLE CatalogSources (Id INTEGER, ProviderId INTEGER, IsEnabled INTEGER);
                CREATE TABLE Providers (Id INTEGER, IsEnabled INTEGER);
                CREATE TABLE Availabilities (TitleId INTEGER, CatalogSourceId INTEGER, RemovedAt TEXT);
                INSERT INTO Titles VALUES (1,'Movie',NULL,NULL,'["IT"]',5,100,1,8,500,7.4), (2,'Movie',NULL,NULL,NULL,5,100,1,NULL,NULL,5);
                INSERT INTO TitleGenres VALUES (1,1),(2,1);
                INSERT INTO TitleEmbeddings VALUES (1,'model-v1',X'0000803F00000000'),(2,'other',X'0000803F00000000');
                INSERT INTO CatalogSources VALUES (1,1,1);
                INSERT INTO Providers VALUES (1,1);
                INSERT INTO Availabilities VALUES (1,1,'2025-01-01'),(2,1,NULL);
                """;
            command.ExecuteNonQuery();
        }
        Should.Throw<InvalidOperationException>(() => BenchmarkCatalogInput.Read(db, "model-v1", 2));
        using (var command = db.CreateCommand())
        {
            command.CommandText = "PRAGMA query_only=ON";
            command.ExecuteNonQuery();
        }
        var catalog = BenchmarkCatalogInput.Read(db, "model-v1", 2);
        catalog.Titles.Single(t => t.Features.TitleId == 1).Features.OriginCountries.ShouldContain("IT");
        catalog.Titles.Single(t => t.Features.TitleId == 2).Embedding.ShouldBeNull();
        var blended = catalog.Titles.Single(t => t.Features.TitleId == 1);
        blended.Stats.VoteBlended.ShouldBe(7.4);
        blended.UsableVoteCount.ShouldBe(600);
        var tmdbOnly = catalog.Titles.Single(t => t.Features.TitleId == 2);
        tmdbOnly.Stats.VoteBlended.ShouldBe(5);
        tmdbOnly.UsableVoteCount.ShouldBe(100);
        var result = TemporalRanking.Score(catalog, [], 1, Day(1), Day(1));
        result.Prior.Titles.Select(x => x.TitleId).ShouldBe(new[] { 1, 2 });
        result.EmbeddingCount.ShouldBe(1);
    }

    [Fact]
    public void Real_level3_trainer_uses_only_training_prefix_and_scores_complete_pool()
    {
        var titles = Enumerable.Range(1, 104).Select(id => Title(id, id % 3)).ToArray();
        var catalog = new BenchmarkCatalog(titles, titles.Select(t => new RecoAvailabilityFact(t.Features.TitleId, 1)).ToArray(),
            [new(1, true, true)], "model-v1", 2);
        var events = Enumerable.Range(1, 100)
            .Select(id => E(id, 1, id, id, id % 2 == 0 ? "Like" : "Dislike")).ToArray();
        var result = TemporalRanking.Score(catalog, events, 1, Day(101), Day(102), new RecoLevel3Model());
        result.Level3.UnavailableReason.ShouldBeNull();
        result.Level3.Titles.Count.ShouldBe(4);
        result.Level3.Titles.All(t => double.IsFinite(t.Score)).ShouldBeTrue();
    }

    private sealed class CapturingModel : IRecoLevel3Model
    {
        public IReadOnlyList<RecoLevel3Example> Examples { get; private set; } = [];
        public IRecoLevel3Scorer? TryTrain(IReadOnlyList<RecoLevel3Example> examples, int minRatings, int minPerClass)
        {
            Examples = examples;
            return new Scorer();
        }
    }

    private sealed class VoteCountModel : IRecoLevel3Model
    {
        public IReadOnlyList<RecoLevel3Example> Examples { get; private set; } = [];
        public IRecoLevel3Scorer? TryTrain(IReadOnlyList<RecoLevel3Example> examples, int minRatings, int minPerClass)
        {
            Examples = examples;
            return new VoteCountScorer();
        }
    }

    private sealed class VoteCountScorer : IRecoLevel3Scorer
    {
        public double Score(float[] features) => features[RecoLevel3.VoteCountFeatureIndex];
    }

    private sealed class Scorer : IRecoLevel3Scorer
    {
        public double Score(float[] features) => features[RecoLevel3.VoteFeatureIndex];
    }

    private sealed class RejectingModel : IRecoLevel3Model
    {
        public IRecoLevel3Scorer? TryTrain(IReadOnlyList<RecoLevel3Example> examples, int minRatings, int minPerClass) => null;
    }
}
