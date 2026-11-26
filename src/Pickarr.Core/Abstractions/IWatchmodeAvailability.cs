using Pickarr.Core.Catalog;

namespace Pickarr.Core.Abstractions;

public interface IWatchmodeAvailability
{
    /// <summary>
    /// Null when Watchmode could not be read. Callers then keep the built-in country list.
    /// </summary>
    Task<WatchmodeAvailability?> GetAsync(CancellationToken cancellationToken = default);
}
