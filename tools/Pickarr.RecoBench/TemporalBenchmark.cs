using Pickarr.Core.Recommendations;

namespace Pickarr.RecoBench;

public sealed record MetricValue(double? Value, int Profiles, string? UnavailableReason);
public sealed record BenchmarkMetricSet(
    MetricValue RecallAt10, MetricValue HitAt10, MetricValue PositiveVsDislikeAuc,
    MetricValue PositiveVsNotInterestedAuc, int AvailableProfiles,
    IReadOnlyDictionary<string, int> UnavailableReasons);
public sealed record FoldCoverage(
    int Profiles, int Events, int PoolTitles, int PoolEmbeddings, int EligibleTitles, int LabelledTitles,
    int PositiveTitles, int DislikeTitles, int NotInterestedTitles,
    int DeletedTitles, int AmbiguousTitles, int IneligibleChangedTitles);
public sealed record TemporalFoldReport(FoldCoverage Coverage, IReadOnlyDictionary<string, BenchmarkMetricSet> Metrics);
public sealed record TemporalBenchmarkReport(
    int ProfilesWithEvents, int ProfilesWithSplit, int ProfilesWithoutSplit,
    int TrainingEvents, IReadOnlyDictionary<string, int> SplitUnavailableReasons,
    TemporalFoldReport Validation, TemporalFoldReport Test);

public static class TemporalBenchmark
{
    private sealed record Observation(BenchmarkRankings Ranking, FoldCoverage Coverage,
        IReadOnlyDictionary<int, string> Labels);

    public static TemporalBenchmarkReport Evaluate(BenchmarkCatalog catalog, IReadOnlyList<TemporalRatingEvent> events)
    {
        var profiles = events.Select(e => e.ProfileId).Distinct().Order().ToArray();
        var validation = new List<Observation>();
        var test = new List<Observation>();
        var trainingEvents = 0;
        var splitReasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var profile in profiles)
        {
            var splitResult = TemporalHistory.Split(profile, events);
            var split = splitResult.Split;
            if (split is null)
            {
                var reason = splitResult.UnavailableReason!;
                splitReasons[reason] = splitReasons.GetValueOrDefault(reason) + 1;
                continue;
            }
            trainingEvents += split.Train.Count;
            validation.Add(Observe(catalog, events, profile, split.ValidationCutoff, split.ValidationCutoff,
                split.Validation));
            test.Add(Observe(catalog, events, profile, split.ValidationCutoff, split.TestCutoff, split.Test));
        }
        return new(profiles.Length, validation.Count, profiles.Length - validation.Count, trainingEvents, splitReasons,
            Aggregate(validation), Aggregate(test));
    }

    private static Observation Observe(BenchmarkCatalog catalog, IReadOnlyList<TemporalRatingEvent> events,
        int profile, DateTime trainCutoff, DateTime cutoff, IReadOnlyList<TemporalRatingEvent> fold)
    {
        var rankings = TemporalRanking.Score(catalog, events, profile, trainCutoff, cutoff);
        var eligible = rankings.Prior.Titles.Select(x => x.TitleId).ToHashSet();
        var labels = new Dictionary<int, string>();
        var deleted = 0;
        var ambiguous = 0;
        var ineligible = 0;
        foreach (var group in fold.GroupBy(e => e.TitleId))
        {
            if (!eligible.Contains(group.Key)) { ineligible++; continue; }
            var last = group.Max(e => e.At);
            var lastEvents = group.Where(e => e.At == last).ToArray();
            var state = TemporalHistory.ReplayBefore(lastEvents, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc))
                [(profile, group.Key)];
            if (state.Status == TemporalRatingStatus.Deleted) deleted++;
            else if (state.Status == TemporalRatingStatus.Ambiguous) ambiguous++;
            else labels.Add(group.Key, state.Value!);
        }
        var coverage = new FoldCoverage(1, fold.Count, rankings.PoolCount, rankings.EmbeddingCount, eligible.Count, labels.Count,
            labels.Count(x => x.Value is "Love" or "Like"), labels.Count(x => x.Value == "Dislike"),
            labels.Count(x => x.Value == "NotInterested"), deleted, ambiguous, ineligible);
        return new(rankings, coverage, labels);
    }

    private static TemporalFoldReport Aggregate(IReadOnlyList<Observation> observations)
    {
        var coverage = new FoldCoverage(observations.Count, observations.Sum(x => x.Coverage.Events),
            observations.Sum(x => x.Coverage.PoolTitles), observations.Sum(x => x.Coverage.PoolEmbeddings),
            observations.Sum(x => x.Coverage.EligibleTitles), observations.Sum(x => x.Coverage.LabelledTitles),
            observations.Sum(x => x.Coverage.PositiveTitles), observations.Sum(x => x.Coverage.DislikeTitles),
            observations.Sum(x => x.Coverage.NotInterestedTitles), observations.Sum(x => x.Coverage.DeletedTitles),
            observations.Sum(x => x.Coverage.AmbiguousTitles), observations.Sum(x => x.Coverage.IneligibleChangedTitles));
        var metrics = new Dictionary<string, BenchmarkMetricSet>();
        foreach (var name in new[] { "prior", "sparse", "embedding", "combined", "reranked", "level3" })
        {
            var recalls = new List<double>();
            var hits = new List<double>();
            var dislikes = new List<double>();
            var notInterested = new List<double>();
            var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var available = 0;
            foreach (var observation in observations)
            {
                var ranking = name switch
                {
                    "prior" => observation.Ranking.Prior,
                    "sparse" => observation.Ranking.Sparse,
                    "embedding" => observation.Ranking.Embedding,
                    "combined" => observation.Ranking.Combined,
                    "reranked" => observation.Ranking.Reranked,
                    _ => observation.Ranking.Level3
                };
                if (ranking.UnavailableReason is not null)
                {
                    reasons[ranking.UnavailableReason] = reasons.GetValueOrDefault(ranking.UnavailableReason) + 1;
                    continue;
                }
                available++;
                var positives = observation.Labels.Count(x => x.Value is "Love" or "Like");
                if (positives > 0)
                {
                    var top = ranking.Titles.Take(10).Count(x =>
                        observation.Labels.TryGetValue(x.TitleId, out var value) && value is "Love" or "Like");
                    recalls.Add((double)top / positives);
                    hits.Add(top > 0 ? 1 : 0);
                }
                var labelled = ranking.Titles.Select((x, index) =>
                    (x.TitleId, Score: name == "reranked" ? -index : x.Score))
                    .Where(x => observation.Labels.ContainsKey(x.TitleId)).ToArray();
                double? Auc(string negative) => RecoEvalMetrics.LikeVsDislikeAuc(labelled
                    .Where(x => observation.Labels[x.TitleId] is "Love" or "Like"
                        || observation.Labels[x.TitleId] == negative)
                    .Select(x => (observation.Labels[x.TitleId] is "Love" or "Like", x.Score)).ToArray());
                if (Auc("Dislike") is double d) dislikes.Add(d);
                if (Auc("NotInterested") is double n) notInterested.Add(n);
            }
            MetricValue Metric(List<double> values, string noLabels) => values.Count == 0
                ? new(null, 0, available == 0 ? "ranking_unavailable_for_all_profiles" : noLabels)
                : new(values.Average(), values.Count, null);
            metrics.Add(name, new(Metric(recalls, "no_eligible_positives"), Metric(hits, "no_eligible_positives"),
                Metric(dislikes, "positive_or_dislike_missing"), Metric(notInterested, "positive_or_notinterested_missing"),
                available, reasons));
        }
        return new(coverage, metrics);
    }
}
