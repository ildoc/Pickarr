using Hangfire;
using Microsoft.Extensions.Logging;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Domain;

namespace Pickarr.Infrastructure.Jobs;

public sealed class DetailCastJob(
    IDetailCastService detailCast,
    ISyncRunStore syncRuns,
    ILogger<DetailCastJob> logger)
{
    public const string RecurringJobId = "detail-cast";
    public const int BatchSize = 50;
    public const int MaxBatches = 20;

    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    public async Task ExecuteAsync(bool isManual = false, CancellationToken cancellationToken = default)
    {
        var trigger = isManual ? SyncRunTriggers.Manual : SyncRunTriggers.Schedule;
        var run = await syncRuns.StartAsync(
            SyncRunKinds.DetailCast,
            catalogSourceId: null,
            trigger,
            cancellationToken: cancellationToken);

        try
        {
            var processed = 0;
            var updated = 0;
            var failed = 0;
            var empty = 0;
            string? lastError = null;

            for (var i = 0; i < MaxBatches; i++)
            {
                var result = await detailCast.ProcessBatchAsync(BatchSize, cancellationToken: cancellationToken);
                processed += result.Processed;
                updated += result.Updated;
                failed += result.Failed;
                empty += result.Empty;
                if (!string.IsNullOrWhiteSpace(result.LastError))
                {
                    lastError = result.LastError;
                }

                if (result.Processed == 0)
                {
                    break;
                }
            }

            var tracked = await syncRuns.GetAsync(run.Id, cancellationToken);
            if (tracked is not null)
            {
                tracked.PagesFetched = processed;
                tracked.TitlesUpdated = updated;
                tracked.TitlesAdded = empty;
                tracked.ErrorMessage = lastError;
                await syncRuns.UpdateAsync(tracked, cancellationToken);
            }

            if (failed > 0)
            {
                if (processed > failed)
                {
                    await syncRuns.PartiallySucceedAsync(run.Id, lastError, cancellationToken);
                }
                else
                {
                    await syncRuns.FailAsync(run.Id, lastError ?? "All detail cast batches failed.", cancellationToken);
                }
            }
            else
            {
                await syncRuns.SucceedAsync(run.Id, cancellationToken);
            }

            logger.LogInformation(
                "Detail cast backfill processed {Processed} titles ({Updated} with cast, {Empty} empty, {Failed} failed)",
                processed,
                updated,
                empty,
                failed);
        }
        catch (Exception ex)
        {
            await syncRuns.FailAsync(run.Id, ex.Message, cancellationToken);
            throw;
        }
    }
}
