using Pickarr.Core.Catalog;
using Shouldly;

namespace Pickarr.Core.Tests;

public sealed class CatalogOffersTests
{
    [Fact]
    public void Without_live_watchmode_data_the_built_in_country_list_is_used()
    {
        CatalogOffers.AvailableCountries("now-tv", "406", live: null).ShouldBe(["GB", "IE", "IT"]);
    }

    [Fact]
    public void A_country_must_be_on_the_source_and_on_the_account_plan()
    {
        var live = new WatchmodeAvailability(
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                ["406"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GB", "IE", "IT" },
            },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "US", "IT" });

        CatalogOffers.AvailableCountries("now-tv", "406", live).ShouldBe(["IT"]);
    }
}
