using Pickarr.Core.Catalog;

namespace Pickarr.Core.Abstractions;

public interface IArrSeasonMonitoringService
{
    Task<ArrDetailDto> SetSeasonMonitoringAsync(
        int titleId,
        int seasonNumber,
        bool monitored,
        CancellationToken cancellationToken = default,
        int? profileId = null);
}
