using Pickarr.Core.Domain;
using Pickarr.Core.Domain.Entities;
using Pickarr.Core.Metadata;
using Pickarr.Infrastructure.Persistence;

namespace Pickarr.Infrastructure.Metadata;

/// <summary>Keeps the synthetic Stand-up genre (D-06) in step with the title's TMDB keywords.</summary>
internal static class StandUpGenre
{
    public static async Task EnsureGenreAsync(
        PickarrDbContext db,
        IEnumerable<string> languages,
        CancellationToken cancellationToken)
    {
        if (await db.Genres.FindAsync([SyntheticGenres.StandUp], cancellationToken) is null)
        {
            db.Genres.Add(new Genre { Id = SyntheticGenres.StandUp });
        }

        var all = languages
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Append(LanguageFallback.English)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var language in all)
        {
            if (await db.GenreLocalizations.FindAsync([SyntheticGenres.StandUp, language], cancellationToken) is null)
            {
                db.GenreLocalizations.Add(new GenreLocalization
                {
                    GenreId = SyntheticGenres.StandUp,
                    Language = language,
                    Name = SyntheticGenres.StandUpName,
                });
            }
        }
    }

    /// <summary>Requires <see cref="Title.TitleGenres"/> to be loaded. Returns whether the link changed.</summary>
    public static bool SyncLink(PickarrDbContext db, Title title, bool isStandUp)
    {
        var link = title.TitleGenres.FirstOrDefault(tg => tg.GenreId == SyntheticGenres.StandUp);
        if (isStandUp == link is not null)
        {
            return false;
        }

        if (link is null)
        {
            title.TitleGenres.Add(new TitleGenre { TitleId = title.Id, GenreId = SyntheticGenres.StandUp });
        }
        else
        {
            db.TitleGenres.Remove(link);
        }

        return true;
    }
}
