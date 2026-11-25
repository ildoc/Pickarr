using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Domain;
using Pickarr.Core.Domain.Entities;
using Pickarr.Core.Recommendations;
using Pickarr.Infrastructure.Catalog;
using Pickarr.Infrastructure.Metadata.Reco;
using Pickarr.Infrastructure.Persistence;
using Shouldly;

namespace Pickarr.Infrastructure.Tests;

public sealed class RecoSimilarServiceTests
{
    private const int Query = 1, Sparse = 126, Late = 127, Rated = 128, Seen = 129, Library = 130, Disabled = 131;

    [Theory]
    [InlineData(true, false, 1, Sparse)]
    [InlineData(true, true, 1, Late)]
    [InlineData(true, true, 6, Late)]
    [InlineData(false, true, 6, Late)]
    public async Task QuerySimilar_matches_exhaustive_oracle(
        bool queryEmbedding, bool graph, int k, int expectedWinner)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new PickarrDbContext(new DbContextOptionsBuilder<PickarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        // 124 earlier eligible titles are perfect sparse matches but have poor dense scores.
        // Sparse has only the common movie term. Late is behind Sparse locally but wins with its edge.
        var rows = Enumerable.Range(1, Disabled).Select(id => new Row(id,
            English: id != Sparse && id != Late,
            X: id switch
            {
                Query => queryEmbedding ? 1f : null,
                Sparse => null,
                Late => .62f,
                >= Rated => 1f,
                _ => .2f,
            },
            Rank: graph && (id == Late || id >= Rated) ? 1 : null)).ToList();
        await SeedAsync(db, rows);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var service = new RecoQueryService(db, Substitute.For<ISettingsStore>(), clock);
        var result = await service.QuerySimilarAsync(new SimilarTitlesQuery(Query, Limit: k, ProfileId: 1));
        var expected = Oracle(rows, k);

        expected[0].Id.ShouldBe(expectedWinner);
        result.Items.Select(x => x.Id).ShouldBe(expected.Select(x => x.Id));
        result.Items.Select(x => x.Reco.Similarity!.Value).ShouldBe(expected.Select(x => Math.Round(x.Score, 4)));
        result.Items.ShouldAllBe(x => x.Places.Count == 1);
    }

    private sealed record Row(int Id, bool English, float? X, int? Rank);

    // Independent closed-form oracle for this two-term corpus: movie is ubiquitous (IDF=1);
    // all English rows have the query's identical vector, others share just its movie component.
    // Dense vectors are unit vectors and the query is (1,0), so dense cosine is exactly x.
    private static List<(int Id, double Score)> Oracle(List<Row> rows, int k)
    {
        var corpus = rows.Where(x => x.Id != Disabled).ToList();
        var english = 0.3 * (1 + Math.Log((corpus.Count + 1d) / (corpus.Count(x => x.English) + 1d)));
        var movieOnly = 0.5 / Math.Sqrt(0.25 + english * english);
        var hasQueryVector = corpus.Single(x => x.Id == Query).X.HasValue;
        return corpus.Where(x => x.Id is not (Query or Rated or Seen or Library))
            .Select(x =>
            {
                var local = hasQueryVector && x.X is float dense
                    ? (dense + 1d) / 2
                    : x.English ? 1d : movieOnly;
                var boost = x.Rank is int rank ? 0.15 * (21 - rank) / 20 : 0;
                return (x.Id, Score: local + boost);
            })
            .OrderByDescending(x => x.Score).ThenBy(x => x.Id).Take(k).ToList();
    }

    private static async Task SeedAsync(PickarrDbContext db, List<Row> rows)
    {
        var provider = new Provider { Key = "fixture", DisplayName = "Fixture", ProviderType = "streaming", IsEnabled = true };
        var enabled = new CatalogSource { Provider = provider, CountryCode = "IT", MetadataLanguage = "en", IsEnabled = true };
        var disabled = new CatalogSource { Provider = provider, CountryCode = "GB", MetadataLanguage = "en", IsEnabled = false };
        db.Profiles.AddRange(
            new Profile { Id = 1, Name = "One", Color = "blue", IsAdmin = true },
            new Profile { Id = 2, Name = "Two", Color = "red" });
        foreach (var row in rows)
        {
            db.Titles.Add(new Title
            {
                Id = row.Id,
                OriginalTitle = $"T{row.Id}",
                MediaType = MediaTypes.Movie,
                Category = TitleCategories.Movie,
                OriginalLanguage = row.English ? "en" : null,
                MetadataStatus = MetadataStatuses.Ready,
            });
            db.Availabilities.Add(new Availability { TitleId = row.Id, CatalogSource = row.Id == Disabled ? disabled : enabled });
            if (row.X is float x)
            {
                db.TitleEmbeddings.Add(new TitleEmbedding
                {
                    TitleId = row.Id,
                    ModelId = RecoModelCatalog.DefaultModelId,
                    TextHash = "fixture",
                    Vector = RecoEmbeddingService.FloatsToBytes([x, MathF.Sqrt(1 - x * x)]),
                });
            }

            if (row.Rank is int rank)
            {
                db.TitleTmdbRecommendations.Add(new TitleTmdbRecommendation { SourceTitleId = Query, RelatedTitleId = row.Id, Rank = rank });
            }
        }

        db.Ratings.AddRange(
            new Rating { TitleId = Rated, ProfileId = 1, Value = RatingValues.Like },
            new Rating { TitleId = Sparse, ProfileId = 2, Value = RatingValues.Like });
        db.Watched.Add(new Watched { TitleId = Seen, ProfileId = 1 });
        db.ArrLibraryItems.Add(new ArrLibraryItem { TitleId = Library, Kind = "radarr", ArrTitle = "Fixture", State = "Downloaded" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }
}
