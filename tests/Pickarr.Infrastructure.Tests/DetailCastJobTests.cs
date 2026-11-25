using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Domain;
using Pickarr.Core.Domain.Entities;
using Pickarr.Infrastructure.Jobs;
using Shouldly;

namespace Pickarr.Infrastructure.Tests;

public sealed class DetailCastJobTests
{
    [Fact]
    public async Task ExecuteAsync_processes_at_most_the_configured_number_of_batches()
    {
        var service = Substitute.For<IDetailCastService>();
        var runs = Substitute.For<ISyncRunStore>();
        var run = new SyncRun
        {
            Id = Guid.NewGuid(),
            Kind = SyncRunKinds.DetailCast,
            TriggeredBy = SyncRunTriggers.Schedule,
            Status = SyncRunStatuses.Running,
        };
        runs.StartAsync(
                SyncRunKinds.DetailCast,
                null,
                SyncRunTriggers.Schedule,
                null,
                Arg.Any<CancellationToken>())
            .Returns(run);
        service.ProcessBatchAsync(Arg.Any<int>(), 30, Arg.Any<CancellationToken>())
            .Returns(new DetailCastBatchResult(1, 1, 0, 0));

        var sut = new DetailCastJob(service, runs, NullLogger<DetailCastJob>.Instance);

        await sut.ExecuteAsync(cancellationToken: CancellationToken.None);

        await service.Received(DetailCastJob.MaxBatches).ProcessBatchAsync(
            DetailCastJob.BatchSize,
            30,
            Arg.Any<CancellationToken>());
        await runs.Received(1).SucceedAsync(run.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_records_partial_failure_and_manual_trigger()
    {
        var service = Substitute.For<IDetailCastService>();
        var runs = Substitute.For<ISyncRunStore>();
        var run = new SyncRun
        {
            Id = Guid.NewGuid(),
            Kind = SyncRunKinds.DetailCast,
            TriggeredBy = SyncRunTriggers.Manual,
            Status = SyncRunStatuses.Running,
        };
        runs.StartAsync(
                SyncRunKinds.DetailCast,
                null,
                SyncRunTriggers.Manual,
                null,
                Arg.Any<CancellationToken>())
            .Returns(run);
        service.ProcessBatchAsync(Arg.Any<int>(), 30, Arg.Any<CancellationToken>())
            .Returns(
                new DetailCastBatchResult(2, 1, 1, 0, "temporary timeout"),
                new DetailCastBatchResult(0, 0, 0, 0));

        var sut = new DetailCastJob(service, runs, NullLogger<DetailCastJob>.Instance);

        await sut.ExecuteAsync(isManual: true, cancellationToken: CancellationToken.None);

        await runs.Received(1).PartiallySucceedAsync(
            run.Id,
            "temporary timeout",
            Arg.Any<CancellationToken>());
        await runs.DidNotReceive().SucceedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
