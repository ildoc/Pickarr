using Pickarr.Core.Domain;
using Pickarr.Core.Domain.Entities;
using Pickarr.Core.Recommendations;
using Pickarr.Infrastructure.Persistence;

namespace Pickarr.PathBench;

internal static class Fixture
{
    public const int PoolSize = 132;
    public const int Outside = 133;

    public static async Task Seed(PickarrDbContext db, DateTime now)
    {
        await db.Database.EnsureCreatedAsync();
        db.Profiles.Add(new Profile { Id = 1, Name = "Synthetic", IsAdmin = true, Color = "blue", CreatedAt = now });
        db.Providers.Add(new Provider { Id = 1, Key = "fixture", DisplayName = "Fixture", ProviderType = "streaming", IsEnabled = true, CreatedAt = now });
        db.CatalogSources.Add(new CatalogSource { Id = 1, ProviderId = 1, CountryCode = "IT", MetadataLanguage = "en-US", IsEnabled = true });
        db.Genres.Add(new Genre { Id = 1 });
        // 1 queries a mixed pool. 6 queries the same pool but has a rank-two edge
        // to an embedding runner-up. 131 is the only missing vector.
        for (var id = 1; id <= Outside + 1; id++)
        {
            var oov = id == Outside + 1;
            db.Titles.Add(new Title
            {
                Id = id, Category = oov ? TitleCategories.Series : TitleCategories.Movie,
                MediaType = oov ? MediaTypes.Series : MediaTypes.Movie, OriginalTitle = $"T{id}",
                MetadataStatus = MetadataStatuses.Ready, CreatedAt = now, UpdatedAt = now
            });
            if (id <= PoolSize)
                db.Availabilities.Add(new Availability { TitleId = id, CatalogSourceId = 1, FirstSeenAt = now, LastSeenAt = now });
            if (id is 132 or Outside)
                db.TitleGenres.Add(new TitleGenre { TitleId = id, GenreId = 1 });
            if (id <= PoolSize && id != 131)
            {
                var cosine = id switch { 1 or 3 or 4 or 5 => 1f, 6 => 0.999f, 130 => 0.8f, 132 => 0.96f, _ => 0.2f };
                var vector = new float[RecoModelCatalog.EmbeddingDimensions];
                vector[0] = cosine;
                vector[1] = MathF.Sqrt(1 - cosine * cosine);
                var bytes = new byte[vector.Length * sizeof(float)];
                Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
                db.TitleEmbeddings.Add(new TitleEmbedding
                {
                    TitleId = id, ModelId = RecoModelCatalog.DefaultModelId, Vector = bytes, TextHash = "fixture", UpdatedAt = now
                });
            }
        }
        await db.SaveChangesAsync();
    }

    public static async Task PrepareQueries(PickarrDbContext db, DateTime now)
    {
        db.Ratings.Add(new Rating { ProfileId = 1, TitleId = 3, Value = RatingValues.Like, RatedAt = now });
        db.Watched.Add(new Watched { ProfileId = 1, TitleId = 4, WatchedAt = now });
        db.ArrLibraryItems.Add(new ArrLibraryItem { TitleId = 5, Kind = ArrKinds.Radarr, ArrId = 5, ArrTitle = "T5", State = ArrStates.Downloaded, SyncedAt = now });
        // Both rank-one edges test filters (different queries; ranks within a
        // source are distinct). Rank two still promotes 130 over a cosine of 1.
        db.TitleTmdbRecommendations.AddRange(
            new TitleTmdbRecommendation { SourceTitleId = 1, RelatedTitleId = 4, Rank = 1 },
            new TitleTmdbRecommendation { SourceTitleId = 6, RelatedTitleId = 3, Rank = 1 },
            new TitleTmdbRecommendation { SourceTitleId = 6, RelatedTitleId = 130, Rank = 2 });
        await db.SaveChangesAsync();
    }
}
