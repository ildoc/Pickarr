using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Domain.Entities;
using Pickarr.Core.Metadata;
using Pickarr.Infrastructure.Persistence;

namespace Pickarr.Infrastructure.Metadata;

public sealed class DetailCastService(
    PickarrDbContext db,
    IMetadataProvider metadata,
    IClock clock,
    ILogger<DetailCastService> logger) : IDetailCastService
{
    public async Task<DetailCastBatchResult> ProcessBatchAsync(
        int limit = 50,
        int olderThanDays = 30,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var cutoff = now.AddDays(-olderThanDays);
        var retryCutoff = now.AddHours(-1);
        var batch = await db.Titles
            .AsNoTracking()
            .Where(t =>
                t.TmdbId != null
                && t.TmdbType != null
                && (t.CastFetchedAt == null || t.CastFetchedAt < cutoff)
                && (t.CastFetchAttemptAt == null || t.CastFetchAttemptAt < retryCutoff))
            .OrderBy(t => t.CastFetchAttemptAt == null ? 0 : 1)
            .ThenBy(t => t.CastFetchAttemptAt)
            .ThenBy(t => t.CastFetchedAt)
            .ThenBy(t => t.Id)
            .Take(limit)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        var updated = 0;
        var failed = 0;
        var empty = 0;
        string? lastError = null;

        foreach (var titleId in batch)
        {
            var title = await db.Titles
                .Include(t => t.CastMembers)
                .ThenInclude(member => member.Person)
                .SingleAsync(t => t.Id == titleId, cancellationToken);

            try
            {
                var isMovie = string.Equals(title.TmdbType, "movie", StringComparison.OrdinalIgnoreCase);
                var details = isMovie
                    ? await metadata.GetMovieAsync(title.TmdbId!.Value, "en-US", cancellationToken)
                    : await metadata.GetTvAsync(title.TmdbId!.Value, "en-US", cancellationToken);

                if (details is null)
                {
                    throw new InvalidOperationException($"TMDB returned no title details for {title.TmdbType}/{title.TmdbId}.");
                }

                var cast = details.DetailCast ?? [];
                await ReplaceCastAsync(title, cast, now, cancellationToken);
                title.CastFetchedAt = now;
                title.CastFetchAttemptAt = now;
                await db.SaveChangesAsync(cancellationToken);

                if (cast.Count == 0)
                {
                    empty++;
                }
                else
                {
                    updated++;
                }
            }
            catch (TmdbResourceNotFoundException ex)
            {
                db.ChangeTracker.Clear();
                await MarkCastAttemptAsync(titleId, now, cancellationToken);
                logger.LogWarning(ex, "Detail cast title not found for {TitleId}", titleId);
                lastError = ex.Message;
                failed++;
            }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                await MarkCastAttemptAsync(titleId, now, cancellationToken);
                logger.LogWarning(ex, "Detail cast backfill failed for title {TitleId}", titleId);
                lastError = ex.Message;
                failed++;
            }
        }

        return new DetailCastBatchResult(batch.Count, updated, failed, empty, lastError);
    }

    private async Task MarkCastAttemptAsync(int titleId, DateTime attemptedAt, CancellationToken cancellationToken)
    {
        var title = await db.Titles.SingleAsync(t => t.Id == titleId, cancellationToken);
        title.CastFetchAttemptAt = attemptedAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ReplaceCastAsync(
        Title title,
        IReadOnlyList<TmdbDetailCastMember> cast,
        DateTime fetchedAt,
        CancellationToken cancellationToken)
    {
        var members = new List<TitleCastMember>();
        var position = 0;
        foreach (var item in cast.Take(12))
        {
            var person = await db.CastPeople.FindAsync([item.PersonId], cancellationToken);
            var imdbCutoff = fetchedAt.AddDays(-30);
            var needsImdbRefresh = person?.ImdbFetchedAt is null
                || person.ImdbFetchedAt < imdbCutoff;
            var imdbId = person?.ImdbId;
            var imdbFetchedAt = person?.ImdbFetchedAt;

            if (needsImdbRefresh)
            {
                var externalIds = await metadata.GetPersonExternalIdsAsync(item.PersonId, cancellationToken);
                imdbId = externalIds?.ImdbId;
                imdbFetchedAt = fetchedAt;
            }

            if (person is null)
            {
                person = new CastPerson
                {
                    Id = item.PersonId,
                    Name = item.Name,
                    ProfilePath = item.ProfilePath,
                    ImdbId = imdbId,
                    ImdbFetchedAt = imdbFetchedAt,
                };
                db.CastPeople.Add(person);
            }
            else
            {
                person.Name = item.Name;
                person.ProfilePath = item.ProfilePath;
                person.ImdbId = imdbId;
                person.ImdbFetchedAt = imdbFetchedAt;
            }

            members.Add(new TitleCastMember
            {
                TitleId = title.Id,
                PersonId = person.Id,
                Person = person,
                Position = position++,
            });
        }

        db.TitleCastMembers.RemoveRange(title.CastMembers);
        title.CastMembers.Clear();
        foreach (var member in members)
        {
            title.CastMembers.Add(member);
        }
    }
}
