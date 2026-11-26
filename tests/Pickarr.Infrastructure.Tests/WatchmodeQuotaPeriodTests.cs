using Pickarr.Infrastructure.Catalog;
using Shouldly;

namespace Pickarr.Infrastructure.Tests;

public sealed class WatchmodeQuotaPeriodTests
{
    [Fact]
    public void Remaining_days_on_the_26th_include_today_through_the_end_of_a_30_day_month()
    {
        var now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

        var (daysLeft, daysInPeriod) = WatchmodeQuotaUsageReader.RemainingDays(now, billingDayOfMonth: 1);

        daysInPeriod.ShouldBe(30);
        daysLeft.ShouldBe(5);
    }

    [Fact]
    public void Remaining_days_follow_the_billing_day_when_it_is_not_the_first()
    {
        var now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

        var (daysLeft, daysInPeriod) = WatchmodeQuotaUsageReader.RemainingDays(now, billingDayOfMonth: 15);

        daysInPeriod.ShouldBe(30);
        daysLeft.ShouldBe(19);
    }

    [Fact]
    public void The_first_day_of_a_period_still_has_the_whole_period_left()
    {
        var now = new DateTime(2026, 11, 1, 0, 30, 0, DateTimeKind.Utc);

        var (daysLeft, daysInPeriod) = WatchmodeQuotaUsageReader.RemainingDays(now, billingDayOfMonth: 1);

        daysInPeriod.ShouldBe(30);
        daysLeft.ShouldBe(30);
    }
}
