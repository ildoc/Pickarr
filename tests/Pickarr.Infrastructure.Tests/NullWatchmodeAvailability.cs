using Pickarr.Core.Abstractions;
using Pickarr.Core.Catalog;

namespace Pickarr.Infrastructure.Tests;

internal sealed class NullWatchmodeAvailability : IWatchmodeAvailability
{
    public static readonly NullWatchmodeAvailability Instance = new();

    public Task<WatchmodeAvailability?> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<WatchmodeAvailability?>(null);
}

internal sealed class FixedWatchmodeAvailability(WatchmodeAvailability availability) : IWatchmodeAvailability
{
    public Task<WatchmodeAvailability?> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<WatchmodeAvailability?>(availability);
}
