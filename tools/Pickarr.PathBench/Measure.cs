using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Domain;
using Pickarr.Core.Domain.Entities;
using Pickarr.Core.Recommendations;
using Pickarr.Infrastructure.Catalog;
using Pickarr.Infrastructure.Metadata.Reco;
using Pickarr.Infrastructure.Persistence;

namespace Pickarr.PathBench;

internal sealed class Clock : IClock
{
    public DateTime UtcNow => new(2026, 11, 23, 11, 42, 42, DateTimeKind.Utc);
}

// Identical product defaults on both sides. No environment/secret lookup.
internal sealed class Defaults : ISettingsStore
{
    public Task<string?> GetRawAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    public Task<bool> IsSetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task SetAsync(string key, string valueJson, bool isSecret, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal static class Measure
{
    private sealed record Rebuild(int[] Order, double EffectiveN, double Ms);
    private sealed record Case(string Name, int Query, int K);
    private sealed record Trial(string Subset, int K, bool NeedsFallback, bool Beyond120, int Expected, int Hits, bool Exact, double[] Timings);

    public static async Task<object> Run(PickarrDbContext db, bool synthetic)
    {
        var clock = new Clock();
        if (synthetic) await Fixture.Seed(db, clock.UtcNow);
        var admin = await db.Profiles.Where(p => p.IsAdmin).Select(p => p.Id).SingleAsync();
        var model = await db.RecoStates.Where(s => s.ProfileId == admin).Select(s => s.ModelId).SingleOrDefaultAsync();
        if (model != null && model != RecoModelCatalog.DefaultModelId)
            throw new InvalidDataException("Active model mismatch.");
        await db.RecoStates.ExecuteUpdateAsync(s => s.SetProperty(x => x.Level3Active, false));
        var build = new RecoBuildService(db, clock, new Defaults(), new RecoLevel3Model());
        var service = new RecoQueryService(db, new Defaults(), clock);
        var pool = (await db.Availabilities.Where(a => a.CatalogSource!.IsEnabled && a.CatalogSource.Provider!.IsEnabled)
            .Select(a => a.TitleId).Distinct().ToListAsync()).ToHashSet();
        async Task<Rebuild> Publish(int profile)
        {
            db.ChangeTracker.Clear();
            var start = Stopwatch.GetTimestamp();
            var version = await build.BuildAsync(profile);
            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var order = await db.Recommendations.Where(r => r.ProfileId == profile && r.Version == version)
                .OrderBy(r => r.Rank).Select(r => r.TitleId).ToArrayAsync();
            if (order.Length != pool.Count || !pool.SetEquals(order)) throw new InvalidDataException("Published pool mismatch.");
            var n = await db.RecoStates.Where(s => s.ProfileId == profile).Select(s => s.EffectiveN).SingleAsync();
            return new(order, n, ms);
        }

        var rebuilds = new List<object>();
        if (synthetic)
        {
            foreach (var (subset, outside, expectedN, expectedFirst) in new[]
            {
                ("no-signals", 0, 0d, 1), ("external-oov", Fixture.Outside + 1, 0d, 1),
                ("external-shared-genre", Fixture.Outside, 1d, 132)
            })
            {
                await db.Ratings.ExecuteDeleteAsync();
                if (outside != 0)
                {
                    db.Ratings.Add(new Rating { ProfileId = admin, TitleId = outside, Value = RatingValues.Like, RatedAt = clock.UtcNow });
                    await db.SaveChangesAsync();
                }
                var result = await Publish(admin);
                rebuilds.Add(new { subset, cases = 1, externalRatings = outside == 0 ? 0 : 1, expectedN,
                    result.EffectiveN, recallAt1 = result.Order.First() == expectedFirst ? 1 : 0,
                    expectedWinnerRank = Array.IndexOf(result.Order, expectedFirst) + 1, result.Ms });
            }
            await Fixture.PrepareQueries(db, clock.UtcNow);
        }
        else
        {
            foreach (var profile in await db.Profiles.OrderBy(p => p.Id).Select(p => p.Id).ToListAsync())
            {
                var outside = (await db.Ratings.AsNoTracking().Where(r => r.ProfileId == profile).ToListAsync())
                    .Where(r => !pool.Contains(r.TitleId)).ToArray();
                var full = await Publish(profile);
                // Counterfactual on working data only; restore this profile's votes
                // before measuring another profile (household prior) or Similar.
                double? top10Overlap = null, massDelta = null;
                if (outside.Length > 0)
                {
                    db.Ratings.RemoveRange(outside);
                    await db.SaveChangesAsync();
                    var control = await Publish(profile);
                    var before = control.Order.Take(10).ToArray();
                    if (before.Length > 0) top10Overlap = (double)before.Intersect(full.Order.Take(10)).Count() / before.Length;
                    massDelta = full.EffectiveN - control.EffectiveN;
                    db.Ratings.AddRange(outside);
                    await db.SaveChangesAsync();
                }
                rebuilds.Add(new { subset = outside.Length > 0 ? "external-explicit" : "no-external-explicit",
                    externalRatings = outside.Length, full.EffectiveN, massDelta, top10Overlap, full.Ms });
            }
        }

        db.ChangeTracker.Clear();
        var oracle = await Oracle.Load(db, admin);
        var cases = new List<Case>();
        if (synthetic)
            cases.AddRange([new("mixed-fallback-winner", 1, 1), new("beyond-120", 1, 3),
                new("graph-promotes-runner-up", 6, 1), new("missing-query-vector", 131, 1)]);
        else
        {
            // Cover absent query vectors even when most of the pool has a vector.
            foreach (var group in pool.GroupBy(oracle.Dense.ContainsKey).OrderBy(g => g.Key))
            {
                var ids = group.Order().ToArray();
                var n = Math.Min(8, ids.Length);
                for (var i = 0; i < n; i++)
                    cases.Add(new(group.Key ? "cached-query" : "missing-query-vector",
                        ids[n == 1 ? 0 : (int)((long)i * (ids.Length - 1) / (n - 1))], 10));
            }
        }
        var trials = new List<Trial>();
        var missingVectorOracleSlots = 0;
        var beyond120OracleQueries = 0;
        var fallbackOracleQueries = 0;
        foreach (var c in cases)
        {
            var expected = oracle.Rank(c.Query, c.K);
            var missing = expected.Count(id => !oracle.Dense.ContainsKey(id));
            missingVectorOracleSlots += missing;
            var needsFallback = missing > 0 && oracle.Dense.ContainsKey(c.Query);
            if (needsFallback) fallbackOracleQueries++;
            var first120ById = pool.Where(id => id != c.Query && !oracle.Excluded.Contains(id)).Order().Take(120).ToHashSet();
            var beyond120 = expected.Any(id => !first120ById.Contains(id));
            if (beyond120) beyond120OracleQueries++;
            var request = new SimilarTitlesQuery(c.Query, Limit: c.K, ProfileId: admin);
            var warm = await service.QuerySimilarAsync(request);
            var actual = warm.Items.Select(i => i.Id).ToArray();
            var timings = new double[5];
            for (var i = 0; i < timings.Length; i++)
            {
                var start = Stopwatch.GetTimestamp();
                var response = await service.QuerySimilarAsync(request);
                timings[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (!actual.SequenceEqual(response.Items.Select(x => x.Id))) throw new InvalidDataException("Unstable ranking.");
            }
            trials.Add(new(c.Name, c.K, needsFallback, beyond120, expected.Length, actual.Intersect(expected).Count(), actual.SequenceEqual(expected), timings));
        }
        return new
        {
            pool = pool.Count, cachedPool = oracle.Dense.Count, rebuilds,
            queryProfileCount = 1, queryCases = cases.Count, missingVectorOracleSlots, fallbackOracleQueries, beyond120OracleQueries,
            similar = trials.GroupBy(t => (t.Subset, t.K, t.NeedsFallback, t.Beyond120)).Select(g =>
            {
                var ms = g.SelectMany(t => t.Timings).Order().ToArray();
                var slots = g.Sum(t => t.Expected);
                return new { g.Key.Subset, g.Key.K, g.Key.NeedsFallback, g.Key.Beyond120, cases = g.Count(), slots, hits = g.Sum(t => t.Hits),
                    recall = slots > 0 ? (double?)g.Sum(t => t.Hits) / slots : null,
                    exactOrdered = g.Count(t => t.Exact), calls = ms.Length,
                    p50Ms = ms[(int)Math.Ceiling(.5 * ms.Length) - 1], p95Ms = ms[(int)Math.Ceiling(.95 * ms.Length) - 1] };
            }).ToArray()
        };
    }
}
