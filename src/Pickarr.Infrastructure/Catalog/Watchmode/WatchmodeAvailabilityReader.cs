using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Catalog;

namespace Pickarr.Infrastructure.Catalog.Watchmode;

public sealed class WatchmodeAvailabilityReader(
    IServiceScopeFactory scopes,
    IClock clock,
    ILogger<WatchmodeAvailabilityReader> logger) : IWatchmodeAvailability
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WatchmodeAvailability? _cached;
    private DateTime _cachedAt;

    public async Task<WatchmodeAvailability?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (Fresh())
        {
            return _cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Fresh())
            {
                return _cached;
            }

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var provider = scope.ServiceProvider.GetRequiredService<IProviderResolver>().Resolve(WatchmodeCatalogProvider.ProviderType);
                if (provider is not WatchmodeCatalogProvider watchmode)
                {
                    return _cached;
                }

                var sources = await watchmode.GetAvailableProvidersAsync(cancellationToken);
                var plan = await watchmode.GetPlanCountriesAsync(cancellationToken);
                if (sources.Count == 0 || plan.Count == 0)
                {
                    logger.LogWarning(
                        "Watchmode returned no sources or plan countries. Catalog countries stay on the built-in list.");
                    return _cached;
                }

                var regions = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
                foreach (var source in sources)
                {
                    regions[source.ExternalId.Trim()] = source.Regions
                        .Select(code => code.Trim().ToUpperInvariant())
                        .Where(code => code.Length > 0)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                }

                _cached = new WatchmodeAvailability(
                    regions,
                    plan.ToHashSet(StringComparer.OrdinalIgnoreCase));
                _cachedAt = clock.UtcNow;
                return _cached;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Watchmode availability could not be loaded. Catalog countries stay on the built-in list.");
                return _cached;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool Fresh() => _cached is not null && clock.UtcNow - _cachedAt < CacheFor;
}
