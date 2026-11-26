using Microsoft.EntityFrameworkCore;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Catalog;
using Pickarr.Core.Domain.Entities;
using Pickarr.Core.Settings;
using Pickarr.Infrastructure.Jobs;
using Pickarr.Infrastructure.Persistence;

namespace Pickarr.Infrastructure.Catalog;

public sealed class CatalogSetupException(string message) : Exception(message);

public sealed record CatalogSetupDto(
    int Quota,
    int Used,
    int DaysLeft,
    int DaysInPeriod,
    string Plan,
    int FreeCountryLimit,
    int CreditsPerCall,
    int FullSyncsPerMonth,
    int DailyRunsPerMonth,
    int DefaultPages,
    IReadOnlyList<CatalogProviderSetupDto> Providers);

public sealed record CatalogProviderSetupDto(
    int Id,
    string Key,
    string Name,
    bool Enabled,
    IReadOnlyList<CatalogCountrySetupDto> Countries);

public sealed record CatalogCountrySetupDto(
    string Code,
    bool Enabled,
    int Pages,
    bool PagesMeasured,
    int IncrementalPages,
    bool FullyImported,
    bool WatchmodeEnabled = true);

public sealed record UpdateCatalogSetupRequest(IReadOnlyList<UpdateCatalogProviderRequest> Providers);

public sealed record UpdateCatalogProviderRequest(int Id, bool Enabled, IReadOnlyList<string> Countries);

public sealed class CatalogConfigurationService(
    PickarrDbContext db,
    ICreditBudget budget,
    ISettingsStore settings,
    ScheduleReconciler reconciler,
    CatalogBootstrap bootstrap,
    IClock clock,
    IWatchmodeAvailability watchmode)
{
    public async Task<CatalogSetupDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await budget.GetSnapshotAsync(cancellationToken);
        var plan = await ReadPlanAsync(cancellationToken);
        var (daysLeft, daysInPeriod) = await ReadWindowAsync(cancellationToken);
        var availability = await watchmode.GetAsync(cancellationToken);
        var providers = await db.Providers.AsNoTracking()
            .Include(provider => provider.CatalogSources)
            .OrderBy(provider => provider.DisplayName)
            .ToListAsync(cancellationToken);

        return new CatalogSetupDto(
            snapshot.Quota,
            snapshot.Used,
            daysLeft,
            daysInPeriod,
            plan,
            CatalogOffers.FreeCountryLimit,
            CatalogOffers.CreditsPerCall,
            CatalogOffers.FullSyncsPerMonth,
            CatalogOffers.DailyRunsPerMonth,
            CatalogOffers.DefaultPagesPerCountry,
            providers.Select(provider => MapProvider(provider, availability)).ToList());
    }

    public async Task<CatalogSetupDto> UpdateAsync(
        UpdateCatalogSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        var providers = await db.Providers
            .Include(provider => provider.CatalogSources)
            .ToListAsync(cancellationToken);
        var byId = providers.ToDictionary(provider => provider.Id);

        var availability = await watchmode.GetAsync(cancellationToken);
        var selected = new List<CatalogCountryCost>();
        foreach (var update in request.Providers)
        {
            if (!byId.TryGetValue(update.Id, out var provider))
            {
                throw new CatalogSetupException($"Unknown provider {update.Id}.");
            }

            var offered = CatalogOffers.AvailableCountries(provider.Key, provider.ExternalId, availability);
            var countries = update.Countries
                .Select(code => code.Trim().ToUpperInvariant())
                .Where(code => code.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var unknown = countries.FirstOrDefault(code => !offered.Contains(code, StringComparer.OrdinalIgnoreCase));
            if (unknown is not null)
            {
                var knownServiceCountry = CatalogOffers.CountriesFor(provider.Key)
                    .Contains(unknown, StringComparer.OrdinalIgnoreCase);
                throw new CatalogSetupException(
                    knownServiceCountry && availability is not null
                        ? $"{provider.DisplayName} {unknown} is not enabled on the Watchmode plan."
                        : $"{provider.DisplayName} does not offer {unknown}.");
            }

            if (update.Enabled && countries.Count == 0)
            {
                throw new CatalogSetupException($"{provider.DisplayName} needs at least one country.");
            }

            provider.IsEnabled = update.Enabled;
            foreach (var code in offered)
            {
                var source = provider.CatalogSources.FirstOrDefault(item =>
                    string.Equals(item.CountryCode, code, StringComparison.OrdinalIgnoreCase));
                if (source is null)
                {
                    source = new CatalogSource
                    {
                        ProviderId = provider.Id,
                        CountryCode = code,
                        MetadataLanguage = CatalogOffers.Language(code),
                    };
                    provider.CatalogSources.Add(source);
                    db.CatalogSources.Add(source);
                }

                source.IsEnabled = countries.Contains(code, StringComparer.OrdinalIgnoreCase);
                if (update.Enabled && source.IsEnabled)
                {
                    selected.Add(new CatalogCountryCost(
                        true,
                        source.LastFullTotalPages is > 0 ? source.LastFullTotalPages.Value : CatalogOffers.DefaultPagesPerCountry,
                        Math.Max(1, source.IncrementalPages),
                        source.LastFullAt is not null,
                        code));
                }
            }

            if (availability is not null)
            {
                foreach (var source in provider.CatalogSources)
                {
                    if (!offered.Contains(source.CountryCode, StringComparer.OrdinalIgnoreCase))
                    {
                        source.IsEnabled = false;
                    }
                }
            }
        }

        var snapshot = await budget.GetSnapshotAsync(cancellationToken);
        var freePlan = await ReadPlanAsync(cancellationToken) != "paid";
        var (daysLeft, daysInPeriod) = await ReadWindowAsync(cancellationToken);
        var estimate = CatalogCreditEstimate.Calculate(
            selected,
            snapshot.Used,
            snapshot.Quota,
            freePlan,
            daysLeft,
            daysInPeriod);
        if (estimate.OverCountryLimit)
        {
            throw new CatalogSetupException(
                $"The free Watchmode plan allows {CatalogOffers.FreeCountryLimit} countries. {estimate.Countries} are selected.");
        }

        if (estimate.OverQuota)
        {
            throw new CatalogSetupException(
                $"This selection needs about {estimate.MonthlyCredits} Watchmode credits in a full month. About {estimate.RemainingCredits} of those fall in the {daysLeft} days left, plus {estimate.FillCredits} to fill new catalogs. {snapshot.Used} of {snapshot.Quota} are already used.");
        }

        await db.SaveChangesAsync(cancellationToken);
        await reconciler.ReconcileAsync(cancellationToken);
        await bootstrap.StartAsync(force: false, cancellationToken);
        return await GetAsync(cancellationToken);
    }

    private static CatalogProviderSetupDto MapProvider(Provider provider, WatchmodeAvailability? availability)
    {
        var existing = provider.CatalogSources.ToDictionary(
            source => source.CountryCode,
            StringComparer.OrdinalIgnoreCase);
        var available = CatalogOffers.AvailableCountries(provider.Key, provider.ExternalId, availability);
        var codes = available.ToList();
        if (availability is not null)
        {
            foreach (var source in provider.CatalogSources.Where(source => source.IsEnabled))
            {
                if (!codes.Contains(source.CountryCode, StringComparer.OrdinalIgnoreCase))
                {
                    codes.Add(source.CountryCode);
                }
            }
        }

        var countries = codes
            .Select(code =>
            {
                existing.TryGetValue(code, out var source);
                var measured = source?.LastFullTotalPages is > 0;
                var onPlan = availability is null || available.Contains(code, StringComparer.OrdinalIgnoreCase);
                return new CatalogCountrySetupDto(
                    code,
                    source?.IsEnabled == true,
                    measured ? source!.LastFullTotalPages!.Value : CatalogOffers.DefaultPagesPerCountry,
                    measured,
                    Math.Max(1, source?.IncrementalPages ?? 3),
                    source?.LastFullAt is not null,
                    onPlan);
            })
            .ToList();
        return new CatalogProviderSetupDto(
            provider.Id,
            provider.Key,
            provider.DisplayName,
            provider.IsEnabled,
            countries);
    }

    private async Task<string> ReadPlanAsync(CancellationToken cancellationToken)
    {
        var raw = await settings.GetRawAsync(SettingsKeys.WatchmodePlan, cancellationToken);
        var plan = raw?.Trim().Trim('"').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(plan) ? "free" : plan;
    }

    private async Task<(int DaysLeft, int DaysInPeriod)> ReadWindowAsync(CancellationToken cancellationToken)
    {
        var raw = await settings.GetRawAsync(SettingsKeys.WatchmodeBillingDayOfMonth, cancellationToken);
        return WatchmodeQuotaUsageReader.RemainingDays(clock.UtcNow, WatchmodeQuotaUsageReader.ParseBillingDay(raw));
    }
}
