using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Arr;
using Pickarr.Core.Catalog;
using Pickarr.Core.Domain;
using Pickarr.Core.Domain.Entities;
using Pickarr.Infrastructure.Persistence;

namespace Pickarr.Infrastructure.Arr;

public sealed class ArrSeasonMonitoringService(
    PickarrDbContext db,
    IArrClientFactory clientFactory,
    IArrLibrarySync librarySync,
    ITitleQueryService titles,
    IClock clock) : IArrSeasonMonitoringService
{
    public async Task<ArrDetailDto> SetSeasonMonitoringAsync(
        int titleId,
        int seasonNumber,
        bool monitored,
        CancellationToken cancellationToken = default,
        int? profileId = null)
    {
        var title = await db.Titles.AsNoTracking()
                        .FirstOrDefaultAsync(t => t.Id == titleId, cancellationToken)
                    ?? throw ArrOperationException.TitleNotFound(titleId);
        if (title.MediaType != MediaTypes.Series)
        {
            throw ArrOperationException.Rejected("Season monitoring is only available for series.");
        }

        if (seasonNumber < 0)
        {
            throw ArrOperationException.Rejected("Season number must be zero or greater.");
        }

        var cached = await db.ArrLibraryItems.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TitleId == titleId && a.Kind == ArrKinds.Sonarr, cancellationToken)
                     ?? throw ArrOperationException.NotInLibrary();
        var client = await clientFactory.TryCreateAsync(ArrKind.Sonarr, cancellationToken)
                     ?? throw ArrOperationException.NotConfigured();
        var options = new { seasonNumber, monitored };

        try
        {
            var updated = await client.SetSeasonMonitoringAsync(
                cached.ArrId,
                seasonNumber,
                monitored,
                cancellationToken);
            await librarySync.UpsertItemAsync(ArrKind.Sonarr, updated, cancellationToken);
            await WriteAuditAsync(
                cached.ArrId,
                titleId,
                options,
                ArrAuditOutcomes.Success,
                error: null,
                profileId,
                cancellationToken);

            var detail = await titles.GetTitleAsync(titleId, sourceId: null, cancellationToken);
            return detail?.Arr
                   ?? throw new InvalidOperationException("Title detail missing after season monitoring update.");
        }
        catch (ArrOperationException)
        {
            throw;
        }
        catch (ArrClientException ex)
        {
            await WriteAuditAsync(
                cached.ArrId,
                titleId,
                options,
                ArrAuditOutcomes.Failed,
                ex.Message,
                profileId,
                cancellationToken);
            throw ArrAddService.MapClientException(ex);
        }
        catch (HttpRequestException ex)
        {
            await WriteAuditAsync(
                cached.ArrId,
                titleId,
                options,
                ArrAuditOutcomes.Failed,
                ex.Message,
                profileId,
                cancellationToken);
            throw ArrOperationException.Unreachable(ex.Message);
        }
    }

    private async Task WriteAuditAsync(
        int arrId,
        int titleId,
        object options,
        string outcome,
        string? error,
        int? profileId,
        CancellationToken cancellationToken)
    {
        db.ArrAuditLogs.Add(new ArrAuditLog
        {
            ProfileId = profileId,
            At = clock.UtcNow,
            Action = ArrAuditActions.Update,
            Kind = ArrKinds.Sonarr,
            ArrId = arrId,
            TitleId = titleId,
            Options = JsonSerializer.Serialize(options, ArrJson.Options),
            Outcome = outcome,
            Error = error,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
