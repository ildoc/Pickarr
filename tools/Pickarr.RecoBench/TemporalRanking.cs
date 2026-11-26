using Pickarr.Core.Abstractions;
using Pickarr.Core.Recommendations;

namespace Pickarr.RecoBench;

public sealed record BenchmarkTitle(
    RecoSparseTitleFeatures Features,
    RecoTitleStats Stats,
    string? EmbeddingModelId,
    float[]? Embedding,
    int UsableVoteCount = 0);

public sealed record BenchmarkCatalog(
    IReadOnlyList<BenchmarkTitle> Titles,
    IReadOnlyList<RecoAvailabilityFact> Availabilities,
    IReadOnlyList<RecoSourceFact> Sources,
    string EmbeddingModelId,
    int EmbeddingDimensions);

public sealed record BenchmarkRankedTitle(int TitleId, double Score);
public sealed record BenchmarkRanking(IReadOnlyList<BenchmarkRankedTitle> Titles, string? UnavailableReason);
public sealed record BenchmarkRankings(
    BenchmarkRanking Prior,
    BenchmarkRanking Sparse,
    BenchmarkRanking Embedding,
    BenchmarkRanking Combined,
    BenchmarkRanking Reranked,
    BenchmarkRanking Level3,
    int PoolCount,
    int EligibleCount,
    int EmbeddingCount,
    IReadOnlyList<string> Limitations);

/// <summary>Temporal scoring of an explicitly supplied current-catalog proxy, without database side effects.</summary>
public static class TemporalRanking
{
    public static BenchmarkRankings Score(
        BenchmarkCatalog catalog,
        IReadOnlyList<TemporalRatingEvent> events,
        int profileId,
        DateTime trainCutoff,
        DateTime scoringCutoff,
        IRecoLevel3Model? level3Model = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(events);
        if (profileId <= 0 || trainCutoff.Kind != DateTimeKind.Utc || scoringCutoff.Kind != DateTimeKind.Utc
            || trainCutoff > scoringCutoff)
            throw new ArgumentException("Invalid profile or cutoff.");
        if (string.IsNullOrWhiteSpace(catalog.EmbeddingModelId) || catalog.EmbeddingDimensions <= 0)
            throw new ArgumentException("An exact embedding model and dimension are required.", nameof(catalog));

        var titles = catalog.Titles.ToDictionary(t => t.Features.TitleId);
        if (titles.Any(x => x.Key <= 0 || x.Value.Stats.TitleId != x.Key))
            throw new ArgumentException("Invalid title identity.", nameof(catalog));
        foreach (var title in titles.Values)
        {
            if ((title.Stats.VoteBlended is double vote && !double.IsFinite(vote))
                || (title.Stats.Popularity is double popularity && !double.IsFinite(popularity)))
                throw new ArgumentException("Non-finite title statistics.", nameof(catalog));
            if (title.Embedding is null)
            {
                if (title.EmbeddingModelId is not null)
                    throw new ArgumentException("Embedding model without a vector.", nameof(catalog));
                continue;
            }

            if (!string.Equals(title.EmbeddingModelId, catalog.EmbeddingModelId, StringComparison.Ordinal)
                || title.Embedding.Length != catalog.EmbeddingDimensions
                || title.Embedding.Any(v => !float.IsFinite(v)))
                throw new ArgumentException("Incompatible embedding.", nameof(catalog));
            var norm = title.Embedding.Sum(v => (double)v * v);
            if (!double.IsFinite(norm) || Math.Abs(norm - 1) > 0.001)
                throw new ArgumentException("Embeddings must be unit vectors.", nameof(catalog));
        }

        var poolIds = RecoMembership.PoolTitleIds(catalog.Availabilities, catalog.Sources);
        if (poolIds.Any(id => !titles.ContainsKey(id)))
            throw new ArgumentException("Pool title metadata is missing.", nameof(catalog));
        var pool = poolIds.Select(id => titles[id]).ToArray();
        var vectors = RecoSparse.BuildTitleVectors(pool.Select(t => t.Features).ToArray());
        var embeddings = pool.Where(t => t.Embedding is not null)
            .ToDictionary(t => t.Features.TitleId, t => t.Embedding!);
        var priors = RecoPrior.Compute(pool.Select(t => t.Stats).ToArray())
            .ToDictionary(x => x.TitleId, x => x.Prior);
        var states = TemporalHistory.ReplayBefore(events, scoringCutoff);
        var excluded = states.Where(x => x.Key.ProfileId == profileId
                && x.Value.Status != TemporalRatingStatus.Deleted)
            .Select(x => x.Key.TitleId).ToHashSet();
        var eligible = poolIds.Where(id => !excluded.Contains(id)).ToArray();
        var scoring = Compute(states, profileId, poolIds, vectors, embeddings, priors);

        BenchmarkRanking Rank(Func<int, double?> select, string missingReason)
        {
            var ranked = eligible.Select(id => (Id: id, Score: select(id))).ToArray();
            if (ranked.Any(x => x.Score is null))
                return new BenchmarkRanking([], missingReason);
            return new BenchmarkRanking(ranked.Select(x => new BenchmarkRankedTitle(x.Id, x.Score!.Value))
                .OrderByDescending(x => x.Score).ThenBy(x => x.TitleId).ToArray(), null);
        }

        var prior = Rank(id => priors[id], "prior_unavailable");
        var sparse = scoring.HasSparse && eligible.Any(id => vectors[id].IsEmpty)
            ? new BenchmarkRanking([], "sparse_coverage_incomplete")
            : scoring.HasSparse
            ? Rank(id => scoring.Sparse[id], "sparse_unavailable")
            : new BenchmarkRanking([], "sparse_profile_unavailable");
        var embedding = scoring.HasEmbeddingSignals
            ? Rank(id => scoring.Knn.TryGetValue(id, out var value) ? value : null, "embedding_coverage_incomplete")
            : new BenchmarkRanking([], "embedding_signals_unavailable");
        var combined = Rank(id => scoring.Combined[id], "combined_unavailable");
        var order = RecoMmr.RerankIds(combined.Titles.Select(x => (x.TitleId, x.Score)).ToArray(),
            (a, b) => vectors[a].Dot(vectors[b]));
        var combinedById = combined.Titles.ToDictionary(x => x.TitleId);
        var reranked = new BenchmarkRanking(order.Select(id => combinedById[id]).ToArray(), null);

        var level3 = new BenchmarkRanking([], "level3_trainer_unavailable");
        if (level3Model is not null)
        {
            var examples = BuildTrainingExamples(events, profileId, trainCutoff, poolIds, titles, vectors, embeddings, priors);
            var positives = examples.Count(x => x.IsLike);
            var model = examples.Count >= RecoLevel3.DefaultMinRatings
                && positives >= RecoEvalMetrics.Level3MinPerClass
                && examples.Count - positives >= RecoEvalMetrics.Level3MinPerClass
                ? level3Model.TryTrain(examples, RecoLevel3.DefaultMinRatings, RecoEvalMetrics.Level3MinPerClass)
                : null;
            if (model is null)
                level3 = new BenchmarkRanking([], "level3_insufficient_training");
            else
                level3 = Rank(id =>
                {
                    var value = model.Score(Features(id, titles, vectors, priors, embeddings, scoring));
                    if (!double.IsFinite(value) || value < 0 || value > 1)
                        throw new InvalidOperationException("Invalid level3 score.");
                    return RecoLearning.FuseScore(scoring.PriorSlot[id], value, scoring.PersonalWeight);
                }, "level3_unavailable");
        }

        return new BenchmarkRankings(prior, sparse, embedding, combined, reranked, level3,
            poolIds.Count, eligible.Length, embeddings.Count,
            ["current_catalog_membership_metadata_and_embeddings_are_approximate",
             "rating_event_history_may_be_incomplete",
             "implicit_watched_library_and_tmdb_graph_not_reconstructed",
             "model_id_does_not_verify_model_artifact_version_or_embedding_freshness",
             "combined_uses_default_weights_and_neutral_missing_embedding",
             "combined_and_sparse_mmr_are_proxies_not_production_ranking",
             "level3_is_unselected_train_only_variant_not_a_production_gate"]);
    }

    private sealed record Scores(
        IReadOnlyDictionary<int, double> Sparse,
        IReadOnlyDictionary<int, double> Knn,
        IReadOnlyDictionary<int, double> PriorSlot,
        IReadOnlyDictionary<int, double> Combined,
        double PersonalWeight,
        bool HasSparse,
        bool HasEmbeddingSignals);

    private static Scores Compute(
        IReadOnlyDictionary<(int ProfileId, int TitleId), TemporalRatingState> states,
        int profileId,
        IReadOnlyList<int> poolIds,
        IReadOnlyDictionary<int, SparseVector> vectors,
        IReadOnlyDictionary<int, float[]> embeddings,
        IReadOnlyDictionary<int, double> priors)
    {
        var signals = states.Where(x => x.Key.ProfileId == profileId && x.Value.Status == TemporalRatingStatus.Rated)
            .OrderBy(x => x.Key.TitleId)
            .ToDictionary(x => x.Key.TitleId,
                x => RecoSignals.WeightForExplicit(x.Value.Value!, RecoSignals.DefaultWeights));
        var profile = RecoSparse.BuildProfile(signals, vectors);
        var sparseRaw = RecoSparse.ScoreAll(profile, vectors);
        var sparse = RecoNormalize.MinMaxByTitle(sparseRaw);
        var haveEmbeddingSignals = signals.Any(x => x.Value != 0 && embeddings.ContainsKey(x.Key));
        var knnRaw = haveEmbeddingSignals ? RecoKnn.ScoreAll(embeddings, signals) : new Dictionary<int, double>();
        var knnUnit = knnRaw.ToDictionary(x => x.Key, x => RecoKnn.ToUnitInterval(x.Value));
        var knn = RecoNormalize.MinMaxByTitle(knnUnit);
        var otherVotes = states.Where(x => x.Value.Status == TemporalRatingStatus.Rated)
            .OrderBy(x => x.Key.ProfileId).ThenBy(x => x.Key.TitleId)
            .Select(x => new RecoHouseholdVote(x.Key.ProfileId, x.Key.TitleId, x.Value.Value!));
        var householdSignals = RecoHousehold.AveragePositive(otherVotes, profileId);
        var householdProfile = RecoSparse.BuildProfile(householdSignals, vectors);
        var household = RecoNormalize.MinMaxByTitle(RecoSparse.ScoreAll(householdProfile, vectors));
        var nEff = signals.Values.Sum(Math.Abs);
        var personalWeight = RecoLearning.PersonalWeight(nEff);
        var householdWeight = RecoHousehold.SlotWeight(nEff, !householdProfile.IsEmpty);
        var priorSlot = poolIds.ToDictionary(id => id,
            id => RecoHousehold.BlendPrior(priors[id], household[id], householdWeight));
        var combined = poolIds.ToDictionary(id => id, id =>
        {
            if (profile.IsEmpty || nEff <= 0)
                return priorSlot[id];
            var personal = haveEmbeddingSignals
                ? 0.4 * sparse[id] + 0.6 * knn.GetValueOrDefault(id, 0.5)
                : sparse[id];
            return RecoLearning.FuseScore(priorSlot[id], personal, personalWeight);
        });
        return new Scores(sparseRaw, knnUnit, priorSlot, combined, personalWeight, !profile.IsEmpty, haveEmbeddingSignals);
    }

    private static List<RecoLevel3Example> BuildTrainingExamples(
        IReadOnlyList<TemporalRatingEvent> events,
        int profileId,
        DateTime trainCutoff,
        IReadOnlyList<int> poolIds,
        IReadOnlyDictionary<int, BenchmarkTitle> titles,
        IReadOnlyDictionary<int, SparseVector> vectors,
        IReadOnlyDictionary<int, float[]> embeddings,
        IReadOnlyDictionary<int, double> priors)
    {
        var states = TemporalHistory.ReplayBefore(events, trainCutoff);
        var pool = poolIds.ToHashSet();
        var result = new List<RecoLevel3Example>();
        foreach (var entry in states.Where(x => x.Key.ProfileId == profileId
                     && x.Value.Status == TemporalRatingStatus.Rated && pool.Contains(x.Key.TitleId))
                     .OrderBy(x => x.Key.TitleId))
        {
            var id = entry.Key.TitleId;
            var lastAt = events.Where(e => e.ProfileId == profileId && e.TitleId == id && e.At < trainCutoff)
                .Max(e => e.At);
            var prefix = TemporalHistory.ReplayBefore(events, lastAt)
                .Where(x => x.Key != (profileId, id)).ToDictionary(x => x.Key, x => x.Value);
            var scoring = Compute(prefix, profileId, poolIds, vectors, embeddings, priors);
            var positive = entry.Value.Value is "Love" or "Like";
            result.Add(new RecoLevel3Example(id, positive, Features(id, titles, vectors, priors, embeddings, scoring)));
        }
        return result;
    }

    private static float[] Features(
        int id,
        IReadOnlyDictionary<int, BenchmarkTitle> titles,
        IReadOnlyDictionary<int, SparseVector> vectors,
        IReadOnlyDictionary<int, double> priors,
        IReadOnlyDictionary<int, float[]> embeddings,
        Scores scores)
    {
        var title = titles[id];
        return RecoLevel3.BuildFeatures(vectors[id], scores.Sparse[id], scores.Knn.GetValueOrDefault(id, 0.5),
            priors[id], title.Features.ReleaseYear, title.Stats.VoteBlended, title.Stats.Popularity,
            scores.HasEmbeddingSignals ? 0.4 : 1, scores.HasEmbeddingSignals ? 0.6 : 0,
            scores.PersonalWeight, scores.HasSparse, scores.HasEmbeddingSignals && embeddings.ContainsKey(id),
            title.UsableVoteCount);
    }
}
