using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pickarr.Core.Recommendations;
using Pickarr.RecoBench;
using Shouldly;

namespace Pickarr.Infrastructure.Tests;

public sealed class RecoTemporalBenchmarkTests : IDisposable
{
    private readonly string root = Path.Combine(FindRepo(), "obj", "reco-benchmark-tests", Guid.NewGuid().ToString("N"));
    private readonly string input;
    private readonly string output;
    private readonly string protectedRoot;
    private static readonly DateTime Epoch = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public RecoTemporalBenchmarkTests()
    {
        input = Path.Combine(root, "input", "synthetic.db");
        output = Path.Combine(root, "output");
        protectedRoot = Path.Combine(root, "protected");
        Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(protectedRoot);
        using var db = new SqliteConnection($"Data Source={input};Pooling=False");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE Titles (Id INTEGER PRIMARY KEY, Category TEXT, OriginalLanguage TEXT, ReleaseYear INTEGER, OriginCountries TEXT, VoteTmdb REAL, VoteTmdbCount INTEGER, Popularity REAL, Name TEXT, OriginalTitle TEXT, VoteImdb REAL, VoteImdbCount INTEGER, VoteBlended REAL);
            CREATE TABLE Profiles (Id INTEGER PRIMARY KEY, Name TEXT);
            CREATE TABLE Ratings (ProfileId INTEGER, TitleId INTEGER, Value TEXT, RatedAt TEXT);
            CREATE TABLE RatingEvents (Id INTEGER PRIMARY KEY, ProfileId INTEGER, TitleId INTEGER, At TEXT, Value TEXT);
            CREATE TABLE TitleGenres (TitleId INTEGER, GenreId INTEGER);
            CREATE TABLE TitleKeywords (TitleId INTEGER, KeywordId INTEGER);
            CREATE TABLE TitleLocalizations (TitleId INTEGER, Language TEXT, Name TEXT, Tagline TEXT, Overview TEXT);
            CREATE TABLE GenreLocalizations (GenreId INTEGER, Language TEXT, Name TEXT);
            CREATE TABLE Keywords (Id INTEGER PRIMARY KEY, Name TEXT);
            CREATE TABLE TitleCredits (TitleId INTEGER, PersonId INTEGER, Role TEXT);
            CREATE TABLE TitleEmbeddings (TitleId INTEGER, ModelId TEXT, Vector BLOB, TextHash TEXT);
            CREATE TABLE CatalogSources (Id INTEGER, ProviderId INTEGER, IsEnabled INTEGER);
            CREATE TABLE Providers (Id INTEGER, IsEnabled INTEGER);
            CREATE TABLE Availabilities (TitleId INTEGER, CatalogSourceId INTEGER);
            CREATE TABLE Settings (Value TEXT);
            INSERT INTO Settings VALUES ('SYNTHETIC_SECRET');
            INSERT INTO Profiles VALUES (1,'SYNTHETIC_NAME'),(2,'SYNTHETIC_NAME');
            WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i<15)
            INSERT INTO Titles SELECT i,'Movie',NULL,NULL,NULL,5,100,1,'SYNTHETIC_TITLE','SYNTHETIC_ORIGINAL',NULL,NULL,5 FROM n;
            INSERT INTO TitleLocalizations VALUES
              (1,'it','Nome italiano',NULL,NULL),
              (1,'en','English Name','Tagline','Overview'),
              (2,'fr','Nom francais',NULL,NULL);
            INSERT INTO GenreLocalizations VALUES (10,'it','Azione'),(10,'en','Action');
            INSERT INTO TitleGenres VALUES (1,10);
            INSERT INTO Keywords VALUES (1,'keyword one'),(2,'keyword two');
            INSERT INTO TitleKeywords VALUES (1,1),(1,2);
            INSERT INTO CatalogSources VALUES (1,1,1);
            INSERT INTO Providers VALUES (1,1);
            INSERT INTO Availabilities SELECT Id,1 FROM Titles;
            INSERT INTO RatingEvents VALUES
              (1,1,1,'2025-01-01','Like'),
              (2,1,2,'2025-01-02','Love'),(3,1,12,'2025-01-02','Like'),
              (4,1,13,'2025-01-02','Dislike'),(5,1,14,'2025-01-02','NotInterested'),
              (6,1,3,'2025-01-03','Like'),(7,1,15,'2025-01-03','Dislike'),
              (8,2,1,'2025-01-01','Love');
            """;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Full_pool_metrics_use_real_order_and_only_explicit_labels()
    {
        using var db = Snapshot.OpenBenchmark(input, Path.Combine(output, "x"), protectedRoot);
        var catalog = BenchmarkCatalogInput.Read(db, RecoModelCatalog.DefaultModelId, RecoModelCatalog.EmbeddingDimensions);
        var events = TemporalInput.Read(db, Epoch.AddDays(10)).Events;
        var report = TemporalBenchmark.Evaluate(catalog, events);
        report.ProfilesWithEvents.ShouldBe(2);
        report.ProfilesWithSplit.ShouldBe(1);
        report.ProfilesWithoutSplit.ShouldBe(1);
        var fold = report.Validation;
        fold.Coverage.EligibleTitles.ShouldBe(14);
        fold.Coverage.LabelledTitles.ShouldBe(4);
        fold.Coverage.PositiveTitles.ShouldBe(2);
        fold.Coverage.DislikeTitles.ShouldBe(1);
        fold.Coverage.NotInterestedTitles.ShouldBe(1);
        var prior = fold.Metrics["prior"];
        prior.RecallAt10.Value.ShouldBe(0.5);
        prior.HitAt10.Value.ShouldBe(1);
        prior.PositiveVsDislikeAuc.Value.ShouldBe(0.5);
        prior.PositiveVsNotInterestedAuc.Value.ShouldBe(0.5);
        prior.RecallAt10.Profiles.ShouldBe(1);
        fold.Metrics["embedding"].UnavailableReasons.ShouldContainKey("embedding_signals_unavailable");
        fold.Metrics["level3"].UnavailableReasons.ShouldContainKey("level3_trainer_unavailable");
        var changed = events.Select(e => e.Id == 6 ? e with { Value = "Dislike" } : e).ToArray();
        var perturbed = TemporalBenchmark.Evaluate(catalog, changed);
        perturbed.Validation.Metrics["prior"].RecallAt10.ShouldBe(prior.RecallAt10);
        perturbed.Validation.Metrics["prior"].PositiveVsDislikeAuc.ShouldBe(prior.PositiveVsDislikeAuc);
        perturbed.Test.Metrics["prior"].RecallAt10.Value.ShouldBeNull();
        perturbed.Test.Metrics["prior"].RecallAt10.UnavailableReason.ShouldBe("no_eligible_positives");
    }

    [Fact]
    public void Model_comparison_requires_complete_embedding_metrics_for_every_model_and_fold()
    {
        using var db = Snapshot.OpenBenchmark(input, Path.Combine(output, "x"), protectedRoot);
        var catalog = BenchmarkCatalogInput.Read(db, RecoModelCatalog.DefaultModelId, RecoModelCatalog.EmbeddingDimensions);
        var events = TemporalInput.Read(db, Epoch.AddDays(10)).Events;
        var report = TemporalBenchmark.Evaluate(catalog, events);

        ModelComparisonRunner.HasComparableEmbeddingQuality(report, report).ShouldBeFalse();
    }

    [Fact]
    public void Fresh_quality_catalog_requires_and_assigns_vectors_for_the_entire_pool()
    {
        using var db = Snapshot.OpenBenchmark(input, Path.Combine(output, "x"), protectedRoot);
        var catalog = BenchmarkCatalogInput.Read(db, RecoModelCatalog.DefaultModelId, RecoModelCatalog.EmbeddingDimensions);
        var poolIds = RecoMembership.PoolTitleIds(catalog.Availabilities, catalog.Sources).ToArray();
        var vectors = poolIds.ToDictionary(id => id, _ =>
        {
            var vector = new float[RecoModelCatalog.EmbeddingDimensions];
            vector[0] = 1;
            return vector;
        });

        var qualityCatalog = ModelComparisonRunner.BuildQualityCatalog(
            catalog, vectors, "synthetic-model", RecoModelCatalog.EmbeddingDimensions);

        qualityCatalog.EmbeddingModelId.ShouldBe("synthetic-model");
        qualityCatalog.Titles.Where(title => poolIds.Contains(title.Features.TitleId))
            .ShouldAllBe(title => title.EmbeddingModelId == "synthetic-model" && title.Embedding != null);
        var incompleteVectors = vectors.Where(pair => pair.Key != poolIds[0])
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        Should.Throw<InvalidOperationException>(() => ModelComparisonRunner.BuildQualityCatalog(
            catalog, incompleteVectors, "synthetic-model", RecoModelCatalog.EmbeddingDimensions));
    }

    [Fact]
    public void Separate_negative_classes_use_explicit_pairwise_comparisons()
    {
        using var db = Snapshot.OpenBenchmark(input, Path.Combine(output, "x"), protectedRoot);
        var catalog = BenchmarkCatalogInput.Read(db, RecoModelCatalog.DefaultModelId, RecoModelCatalog.EmbeddingDimensions);
        catalog = catalog with
        {
            Titles = catalog.Titles.Select(t => t with
            {
                Stats = t.Stats with
                {
                    Popularity = t.Features.TitleId switch { 2 => 100, 12 => 10, 13 => 20, 14 => 1, _ => 5 }
                }
            }).ToArray()
        };
        var events = TemporalInput.Read(db, Epoch.AddDays(10)).Events;
        var prior = TemporalBenchmark.Evaluate(catalog, events).Validation.Metrics["prior"];
        prior.PositiveVsDislikeAuc.Value.ShouldBe(0.5);
        prior.PositiveVsNotInterestedAuc.Value.ShouldBe(1);
        prior.PositiveVsDislikeAuc.Profiles.ShouldBe(1);
        prior.PositiveVsNotInterestedAuc.Profiles.ShouldBe(1);
    }

    [Fact]
    public void Model_text_input_uses_deterministic_localized_text_and_only_the_active_pool()
    {
        using var db = Snapshot.OpenBenchmark(input, Path.Combine(output, "x"), protectedRoot);
        var catalog = BenchmarkCatalogInput.Read(db, RecoModelCatalog.DefaultModelId, RecoModelCatalog.EmbeddingDimensions);

        var texts = BenchmarkTextInput.Read(db, catalog);

        texts.Count.ShouldBe(15);
        texts[0].TitleId.ShouldBe(1);
        texts[0].Text.ShouldBe("English Name. Action. Keywords: keyword one, keyword two. Tagline. Overview");
        texts[1].TitleId.ShouldBe(2);
        texts[1].Text.ShouldBe("Nom francais.");
    }

    [Fact]
    public void Stored_embedding_text_hashes_can_be_matched_without_reading_or_reporting_vectors()
    {
        const string expectedText = "English Name. Action. Keywords: keyword one, keyword two. Tagline. Overview";
        var expectedHash = RecoEmbeddingText.Sha256Hex(expectedText);
        using (var writable = new SqliteConnection($"Data Source={input};Pooling=False"))
        {
            writable.Open();
            using var command = writable.CreateCommand();
            command.CommandText = "INSERT INTO TitleEmbeddings VALUES (1, $model, zeroblob(1536), $hash)";
            command.Parameters.AddWithValue("$model", RecoModelCatalog.DefaultModelId);
            command.Parameters.AddWithValue("$hash", expectedHash);
            command.ExecuteNonQuery();
        }

        using var db = Snapshot.OpenBenchmark(input, Path.Combine(output, "x"), protectedRoot);
        var hashes = BenchmarkTextInput.ReadCurrentEmbeddingTextHashes(db, RecoModelCatalog.DefaultModelId);

        hashes.ShouldContainKey(1);
        hashes[1].ShouldBe(expectedHash);
    }

    [Fact]
    public void Deleted_ambiguous_and_previously_rated_changes_are_counted_not_labelled()
    {
        using var db = Snapshot.OpenBenchmark(input, Path.Combine(output, "x"), protectedRoot);
        var catalog = BenchmarkCatalogInput.Read(db, RecoModelCatalog.DefaultModelId, RecoModelCatalog.EmbeddingDimensions);
        var events = TemporalInput.Read(db, Epoch.AddDays(10)).Events.Concat(
        [
            new TemporalRatingEvent(9, 1, 4, Epoch.AddDays(1), null),
            new TemporalRatingEvent(10, 1, 5, Epoch.AddDays(1), "Like"),
            new TemporalRatingEvent(11, 1, 5, Epoch.AddDays(1), "Dislike"),
            new TemporalRatingEvent(12, 1, 1, Epoch.AddDays(1), "Dislike")
        ]).ToArray();
        var coverage = TemporalBenchmark.Evaluate(catalog, events).Validation.Coverage;
        coverage.Events.ShouldBe(8);
        coverage.DeletedTitles.ShouldBe(1);
        coverage.AmbiguousTitles.ShouldBe(1);
        coverage.IneligibleChangedTitles.ShouldBe(1);
        coverage.LabelledTitles.ShouldBe(4);
    }

    [Fact]
    public void Cli_is_repeatable_private_readonly_and_rejects_unsafe_paths()
    {
        var before = SHA256.HashData(File.ReadAllBytes(input));
        var a = Path.Combine(output, "a.json");
        var b = Path.Combine(output, "b.json");
        Run("benchmark", input, a, "42", "2025-01-11T00:00:00.0000000+00:00", protectedRoot).ShouldBe(3);
        Run("benchmark", input, b, "42", "2025-01-11T00:00:00.0000000+00:00", protectedRoot).ShouldBe(3);
        File.ReadAllBytes(a).ShouldBe(File.ReadAllBytes(b));
        var text = File.ReadAllText(a);
        text.ShouldNotContain("SYNTHETIC_");
        text.ShouldNotContain(root);
        using var json = JsonDocument.Parse(text);
        json.RootElement.GetProperty("dataset").GetProperty("sha256").GetString().ShouldBe(Convert.ToHexString(before));
        json.RootElement.GetProperty("evaluation").GetProperty("validation").GetProperty("coverage")
            .GetProperty("positiveTitles").GetInt32().ShouldBe(2);
        Run("benchmark", input, a, "42", "2025-01-11T00:00:00.0000000+00:00", protectedRoot).ShouldBe(1);
        Run("benchmark", input, Path.Combine(protectedRoot, "bad.json"), "42", "2025-01-11T00:00:00.0000000+00:00", protectedRoot).ShouldBe(1);
        Run("benchmark", input, Path.Combine(output, "bad.json"), "42", "invalid", protectedRoot).ShouldBe(2);
        SHA256.HashData(File.ReadAllBytes(input)).ShouldBe(before);
    }

    [Fact]
    public void Cli_success_only_means_diagnostic_minimum_not_statistical_certification()
    {
        using (var db = new SqliteConnection($"Data Source={input};Pooling=False"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                WITH RECURSIVE n(i) AS (SELECT 16 UNION ALL SELECT i+1 FROM n WHERE i<45)
                INSERT INTO Titles SELECT i,'Movie',NULL,NULL,NULL,5,100,1,'SYNTHETIC_TITLE','SYNTHETIC_ORIGINAL',NULL,NULL,5 FROM n;
                INSERT INTO Availabilities SELECT Id,1 FROM Titles WHERE Id >= 16;
                INSERT INTO RatingEvents SELECT Id+100,1,Id,'2025-01-03','Love' FROM Titles WHERE Id >= 16;
                """;
            cmd.ExecuteNonQuery();
        }
        var path = Path.Combine(output, "enough.json");
        Run("benchmark", input, path, "7", "2025-01-11T00:00:00.0000000+00:00", protectedRoot).ShouldBe(0);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        json.RootElement.GetProperty("status").GetString().ShouldBe("completed_not_certified_sufficient");
        json.RootElement.GetProperty("evaluation").GetProperty("test").GetProperty("coverage")
            .GetProperty("positiveTitles").GetInt32().ShouldBe(31);
    }

    [Fact]
    public void Cli_writes_explicit_insufficient_sample_without_quality_claim()
    {
        using (var db = new SqliteConnection($"Data Source={input};Pooling=False"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM RatingEvents WHERE At > '2025-01-01'";
            cmd.ExecuteNonQuery();
        }
        var path = Path.Combine(output, "small.json");
        Run("benchmark", input, path, "7", "2025-01-11T00:00:00.0000000+00:00", protectedRoot).ShouldBe(3);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        json.RootElement.GetProperty("status").GetString().ShouldBe("insufficient_sample");
        var metric = json.RootElement.GetProperty("evaluation").GetProperty("test").GetProperty("metrics")
            .GetProperty("combined").GetProperty("recallAt10");
        metric.GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        metric.GetProperty("unavailableReason").GetString().ShouldBe("ranking_unavailable_for_all_profiles");
    }

    [Fact]
    public void Invalid_catalog_data_fails_safely_without_report()
    {
        using (var db = new SqliteConnection($"Data Source={input};Pooling=False"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE Titles SET OriginCountries='SYNTHETIC_SECRET' WHERE Id=1";
            cmd.ExecuteNonQuery();
        }
        var path = Path.Combine(output, "invalid.json");
        Run("benchmark", input, path, "7", "2025-01-11T00:00:00.0000000+00:00", protectedRoot).ShouldBe(1);
        File.Exists(path).ShouldBeFalse();
    }

    private static int Run(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(Snapshot).Assembly.Location);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        stdout.ShouldNotContain("SYNTHETIC_");
        stderr.ShouldNotContain("SYNTHETIC_");
        foreach (var path in args.Where(Path.IsPathFullyQualified))
        {
            stdout.ShouldNotContain(path);
            stderr.ShouldNotContain(path);
        }
        return process.ExitCode;
    }

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Pickarr.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException();
    }

    public void Dispose() => Directory.Delete(root, true);
}
