using Hangfire;
using Microsoft.Extensions.Logging;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Domain;
using Pickarr.Core.Metadata;
using Pickarr.Infrastructure.Metadata;

namespace Pickarr.Infrastructure.Jobs;

public sealed class ImdbRatingsJob(
    IImdbRatingsSource source,
    IImdbRatingImporter importer,
    ISyncRunStore syncRuns,
    IBackgroundJobClient jobs,
    ILogger<ImdbRatingsJob> logger)
{
    public const string RecurringJobId = "imdb-ratings";
    public const string Cron = "0 4 * * 0";
    public const double RecommendationRebuildMeanDelta = 0.05;

    public static bool ShouldRebuildRecommendations(AudiencePrior? previous, AudiencePrior current) =>
        previous is not { } before
        || Math.Abs(current.Mean - before.Mean) >= RecommendationRebuildMeanDelta
        || Math.Abs(current.TmdbOffset - before.TmdbOffset) >= RecommendationRebuildMeanDelta;

    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60 * 2)]
    public async Task ExecuteAsync(bool isManual = false, CancellationToken cancellationToken = default)
    {
        var trigger = isManual ? SyncRunTriggers.Manual : SyncRunTriggers.Schedule;
        var run = await syncRuns.StartAsync(
            SyncRunKinds.ImdbRatings,
            catalogSourceId: null,
            trigger,
            cancellationToken: cancellationToken);

        try
        {
            await using var gzip = await source.OpenAsync(cancellationToken);
            var result = await importer.ImportAsync(gzip, cancellationToken);
            var tracked = await syncRuns.GetAsync(run.Id, cancellationToken);
            if (tracked is not null)
            {
                tracked.PagesFetched = result.RowCount;
                tracked.TitlesUpdated = result.TitlesUpdated;
                await syncRuns.UpdateAsync(tracked, cancellationToken);
            }

            if (ShouldRebuildRecommendations(result.PreviousPrior, result.Prior))
            {
                jobs.Enqueue<RecoBuildJob>(job => job.ExecuteAsync(null, false, CancellationToken.None));
            }

            await syncRuns.SucceedAsync(run.Id, cancellationToken);
            logger.LogInformation(
                "IMDb ratings import stored {Rows} rows, dump mean {Mean}, catalog prior {Prior}, TMDB offset {Offset}, updated {Titles} titles",
                result.RowCount,
                result.MeanVote,
                result.Prior.Mean,
                result.Prior.TmdbOffset,
                result.TitlesUpdated);
        }
        catch (Exception ex)
        {
            await syncRuns.FailAsync(run.Id, ex.Message, cancellationToken);
            throw;
        }
    }
}
