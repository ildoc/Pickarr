using Microsoft.EntityFrameworkCore;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Settings;
using Pickarr.Infrastructure.Catalog.Watchmode;
using Pickarr.Infrastructure.Persistence;
using System.Text.Json;

namespace Pickarr.Infrastructure.Catalog;

public sealed class WatchmodeQuotaUsageReader(
    PickarrDbContext db,
    ISettingsStore settingsStore,
    IClock clock) : IQuotaUsageReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<QuotaUsage> GetWatchmodeUsageAsync(CancellationToken cancellationToken = default)
    {
        var latestWithQuota = await db.ApiUsages.AsNoTracking()
            .Where(x => x.Service == WatchmodeApiUsageHandler.ServiceName && x.QuotaUsed != null)
            .OrderByDescending(x => x.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestWithQuota?.QuotaUsed is { } headerUsed)
        {
            return new QuotaUsage(headerUsed, latestWithQuota.QuotaLimit);
        }

        var billingDay = ParseBillingDay(
            await settingsStore.GetRawAsync(SettingsKeys.WatchmodeBillingDayOfMonth, cancellationToken));
        var periodStart = ComputePeriodStart(clock.UtcNow, billingDay);
        var sum = await db.ApiUsages.AsNoTracking()
            .Where(x => x.Service == WatchmodeApiUsageHandler.ServiceName && x.Timestamp >= periodStart)
            .SumAsync(x => (int?)x.Credits, cancellationToken) ?? 0;

        return new QuotaUsage(sum, QuotaLimit: null);
    }

    public static int ParseBillingDay(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 1;
        }

        try
        {
            return Math.Clamp(JsonSerializer.Deserialize<int>(raw, JsonOptions), 1, 28);
        }
        catch (JsonException)
        {
            return 1;
        }
    }

    public static DateTime ComputePeriodStart(DateTime utcNow, int billingDayOfMonth)
    {
        billingDayOfMonth = Math.Clamp(billingDayOfMonth, 1, 28);
        var candidate = new DateTime(utcNow.Year, utcNow.Month, billingDayOfMonth, 0, 0, 0, DateTimeKind.Utc);
        if (utcNow < candidate)
        {
            candidate = candidate.AddMonths(-1);
        }

        return candidate;
    }

    /// <summary>
    /// Days still inside the billing period, including today, and the length of that period.
    /// </summary>
    public static (int DaysLeft, int DaysInPeriod) RemainingDays(DateTime utcNow, int billingDayOfMonth)
    {
        var start = ComputePeriodStart(utcNow, billingDayOfMonth);
        var daysInPeriod = Math.Max(1, (start.AddMonths(1) - start).Days);
        var elapsedBeforeToday = Math.Max(0, (utcNow.Date - start.Date).Days);
        var daysLeft = Math.Clamp(daysInPeriod - elapsedBeforeToday, 1, daysInPeriod);
        return (daysLeft, daysInPeriod);
    }
}
