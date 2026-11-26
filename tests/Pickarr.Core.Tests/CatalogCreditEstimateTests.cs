using Pickarr.Core.Catalog;
using Shouldly;

namespace Pickarr.Core.Tests;

public sealed class CatalogCreditEstimateTests
{
    [Fact]
    public void Three_countries_stay_inside_the_free_plan()
    {
        var result = CatalogCreditEstimate.Calculate(
            [
                Country("US", 32),
                Country("IT", 32),
                Country("GB", 32),
            ],
            used: 0,
            quota: 2500,
            freePlan: true);

        result.Countries.ShouldBe(3);
        result.Catalogs.ShouldBe(3);
        result.FillCredits.ShouldBe(96);
        result.MonthlyCredits.ShouldBe(3 * (3 * 30 + 32 * 4 + 30));
        result.OverQuota.ShouldBeFalse();
        result.OverCountryLimit.ShouldBeFalse();
    }

    [Fact]
    public void A_fourth_country_exceeds_the_free_plan_limit()
    {
        var result = CatalogCreditEstimate.Calculate(
            [Country("US", 32), Country("IT", 32), Country("GB", 32), Country("DE", 32)],
            used: 0,
            quota: 2500,
            freePlan: true);

        result.Countries.ShouldBe(4);
        result.OverCountryLimit.ShouldBeTrue();
    }

    [Fact]
    public void Several_providers_in_one_country_count_as_one_country()
    {
        var result = CatalogCreditEstimate.Calculate(
            [
                Country("US", 32),
                Country("US", 32),
                Country("US", 32),
                Country("US", 32),
            ],
            used: 0,
            quota: 2500,
            freePlan: true);

        result.Countries.ShouldBe(1);
        result.Catalogs.ShouldBe(4);
        result.FillCredits.ShouldBe(128);
        result.OverCountryLimit.ShouldBeFalse();
    }

    [Fact]
    public void Measured_pages_and_credits_already_used_can_exceed_the_quota()
    {
        var result = CatalogCreditEstimate.Calculate(
            [new CatalogCountryCost(true, 400, 3, false, "US")],
            used: 2400,
            quota: 2500,
            freePlan: true);

        result.FillCredits.ShouldBe(400);
        result.OverQuota.ShouldBeTrue();
    }

    [Fact]
    public void Paid_plan_does_not_flag_over_quota_from_the_free_cap()
    {
        var result = CatalogCreditEstimate.Calculate(
            [new CatalogCountryCost(true, 400, 3, false, "US")],
            used: 2400,
            quota: 2500,
            freePlan: false);

        result.OverQuota.ShouldBeFalse();
    }

    [Fact]
    public void A_catalog_that_is_already_full_does_not_count_toward_the_fill()
    {
        var result = CatalogCreditEstimate.Calculate(
            [new CatalogCountryCost(true, 32, 3, true, "US")],
            used: 0,
            quota: 2500,
            freePlan: true);

        result.FillCredits.ShouldBe(0);
        result.MonthlyCredits.ShouldBe(3 * 30 + 32 * 4 + 30);
        result.RemainingCredits.ShouldBe(result.MonthlyCredits);
    }

    [Fact]
    public void Late_in_the_period_counts_only_the_days_still_left()
    {
        var result = CatalogCreditEstimate.Calculate(
            [new CatalogCountryCost(true, 32, 3, true, "US")],
            used: 918,
            quota: 2500,
            freePlan: true,
            daysLeft: 5,
            daysInPeriod: 30);

        var monthly = 3 * 30 + 32 * 4 + 30;
        result.MonthlyCredits.ShouldBe(monthly);
        result.RemainingCredits.ShouldBe(41);
        result.FillCredits.ShouldBe(0);
        result.OverQuota.ShouldBeFalse();
    }

    [Fact]
    public void A_full_month_above_the_quota_is_over_even_when_few_days_remain()
    {
        var result = CatalogCreditEstimate.Calculate(
            [new CatalogCountryCost(true, 600, 3, true, "US")],
            used: 0,
            quota: 2500,
            freePlan: true,
            daysLeft: 1,
            daysInPeriod: 30);

        result.MonthlyCredits.ShouldBe(3 * 30 + 600 * 4 + 30);
        result.RemainingCredits.ShouldBe(84);
        result.OverQuota.ShouldBeTrue();
    }

    [Fact]
    public void Credits_already_used_plus_the_days_left_can_still_pass_the_quota()
    {
        var result = CatalogCreditEstimate.Calculate(
            [new CatalogCountryCost(true, 32, 3, true, "US")],
            used: 2480,
            quota: 2500,
            freePlan: true,
            daysLeft: 5,
            daysInPeriod: 30);

        result.RemainingCredits.ShouldBe(41);
        result.OverQuota.ShouldBeTrue();
    }

    [Fact]
    public void The_one_time_fill_counts_in_full_against_the_days_left()
    {
        var result = CatalogCreditEstimate.Calculate(
            [new CatalogCountryCost(true, 200, 3, false, "US")],
            used: 2300,
            quota: 2500,
            freePlan: true,
            daysLeft: 5,
            daysInPeriod: 30);

        result.FillCredits.ShouldBe(200);
        result.OverQuota.ShouldBeTrue();
    }

    private static CatalogCountryCost Country(string code, int pages) => new(true, pages, 3, false, code);
}
