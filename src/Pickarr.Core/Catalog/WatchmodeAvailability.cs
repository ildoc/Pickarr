namespace Pickarr.Core.Catalog;

/// <summary>
/// Regions Watchmode actually serves for this API key: each source's countries,
/// and the countries enabled on the account plan.
/// </summary>
public sealed record WatchmodeAvailability(
    IReadOnlyDictionary<string, IReadOnlySet<string>> RegionsByExternalId,
    IReadOnlySet<string> PlanCountries)
{
    public bool Allows(string? externalId, string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(countryCode))
        {
            return false;
        }

        var code = countryCode.Trim().ToUpperInvariant();
        if (!PlanCountries.Contains(code))
        {
            return false;
        }

        return RegionsByExternalId.TryGetValue(externalId.Trim(), out var regions) && regions.Contains(code);
    }
}
