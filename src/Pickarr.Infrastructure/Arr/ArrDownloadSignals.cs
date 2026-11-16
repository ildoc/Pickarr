using Microsoft.EntityFrameworkCore;
using Pickarr.Core.Domain;
using Pickarr.Core.Domain.Entities;
using Pickarr.Infrastructure.Persistence;

namespace Pickarr.Infrastructure.Arr;

internal static class ArrDownloadSignals
{
    public static async Task RecordAsync(
        PickarrDbContext db,
        IReadOnlyCollection<ArrLibraryItem> items,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var downloaded = items
            .Where(item => item.TitleId != null && item.State == ArrStates.Downloaded)
            .ToList();
        if (downloaded.Count == 0)
        {
            return;
        }

        var titleIds = downloaded.Select(item => item.TitleId!.Value).Distinct().ToList();
        var adds = await db.ArrAuditLogs.AsNoTracking()
            .Where(a => a.TitleId != null && titleIds.Contains(a.TitleId.Value)
                        && a.ProfileId != null && a.Action == ArrAuditActions.Add
                        && a.Outcome == ArrAuditOutcomes.Success && a.At <= now)
            .Select(a => new { a.ProfileId, a.TitleId, a.Kind })
            .Distinct()
            .ToListAsync(cancellationToken);
        var recorded = await db.ArrAuditLogs.AsNoTracking()
            .Where(a => a.TitleId != null && titleIds.Contains(a.TitleId.Value)
                        && a.Action == ArrAuditActions.Download && a.Outcome == ArrAuditOutcomes.Success)
            .Select(a => new { a.ProfileId, a.TitleId, a.Kind })
            .ToListAsync(cancellationToken);
        var seen = recorded.Select(a => (a.ProfileId, a.TitleId, a.Kind)).ToHashSet();
        seen.UnionWith(db.ArrAuditLogs.Local
            .Where(a => a.Action == ArrAuditActions.Download && a.Outcome == ArrAuditOutcomes.Success)
            .Select(a => (a.ProfileId, a.TitleId, a.Kind)));

        foreach (var add in adds)
        {
            var item = downloaded.FirstOrDefault(item => item.TitleId == add.TitleId && item.Kind == add.Kind);
            if (item is null || !seen.Add((add.ProfileId, add.TitleId, add.Kind)))
            {
                continue;
            }

            db.ArrAuditLogs.Add(new ArrAuditLog
            {
                ProfileId = add.ProfileId,
                TitleId = add.TitleId,
                Kind = add.Kind,
                ArrId = item.ArrId,
                At = now,
                Action = ArrAuditActions.Download,
                Outcome = ArrAuditOutcomes.Success,
                Options = "{}",
            });
        }
    }
}
