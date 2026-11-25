using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Domain;
using Pickarr.Core.Domain.Entities;
using Pickarr.Core.Recommendations;
using Pickarr.Core.Settings;
using Pickarr.Infrastructure.Metadata.Reco;
using Pickarr.Infrastructure.Persistence;

namespace Pickarr.Infrastructure.Catalog;

public sealed class RecoEvalService(
    PickarrDbContext db,
    IClock clock,
    ISettingsStore settings,
    IRecoLevel3Model level3Model) : IRecoEvalService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public async Task<RecoEvalReportDto?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var state = await RecoProfileStates.FindAdminNoTrackingAsync(db, cancellationToken);
        if (state?.LastEvalJson is null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<RecoEvalReportDto>(state.LastEvalJson, JsonOptions);
    }

    public async Task<RecoEvalReportDto> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var sources = await db.CatalogSources.AsNoTracking()
            .Include(s => s.Provider)
            .Select(s => new RecoSourceFact(
                s.Id,
                s.IsEnabled,
                s.Provider != null && s.Provider.IsEnabled))
            .ToListAsync(cancellationToken);

        var enabledSourceIds = RecoMembership.EnabledSourceIds(sources);
        var poolTitleIds = enabledSourceIds.Count == 0
            ? []
            : await db.Availabilities.AsNoTracking()
                .Where(a => enabledSourceIds.Contains(a.CatalogSourceId))
                .Select(a => a.TitleId)
                .Distinct()
                .ToListAsync(cancellationToken);

        var poolIdSet = poolTitleIds.ToHashSet();
        var adminId = await RecoProfileStates.AdminIdAsync(db, cancellationToken);
        var ratings = adminId is int evalProfileId
            ? await db.Ratings.AsNoTracking()
                .Where(r => r.ProfileId == evalProfileId && poolIdSet.Contains(r.TitleId))
                .Select(r => new { r.TitleId, r.Value })
                .ToListAsync(cancellationToken)
            : [];

        var likeCount = ratings.Count(r => RatingValues.IsPositive(r.Value));
        var dislikeCount = ratings.Count(r => string.Equals(r.Value, RatingValues.Dislike, StringComparison.OrdinalIgnoreCase));
        var notInterestedCount = ratings.Count(r =>
            string.Equals(RatingValues.Normalize(r.Value), RatingValues.NotInterested, StringComparison.Ordinal));
        var ratingCount = ratings.Count;
        var lowSample = ratingCount < RecoEvalMetrics.LowSampleWarningThreshold;

        if (poolTitleIds.Count == 0 || ratingCount == 0)
        {
            var empty = new RecoEvalReportDto(
                clock.UtcNow,
                ratingCount,
                likeCount,
                dislikeCount,
                poolTitleIds.Count,
                true,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                "insufficient_data",
                null,
                notInterestedCount,
                poolTitleIds.Count - ratingCount,
                0,
                0,
                0,
                0);
            await PersistAsync(empty, level3Active: false, cancellationToken);
            return empty;
        }

        var titles = await db.Titles.AsNoTracking()
            .Where(t => poolIdSet.Contains(t.Id))
            .Select(t => new TitleRow(
                t.Id,
                t.VoteTmdb,
                t.VoteTmdbCount,
                t.Popularity,
                t.Category,
                t.OriginalLanguage,
                t.ReleaseYear,
                t.OriginCountries))
            .ToListAsync(cancellationToken);

        var titleVectors = await BuildTitleVectorsAsync(poolIdSet, titles, cancellationToken);
        var priors = RecoPrior.Compute(
            titles.Select(t => new RecoTitleStats(t.Id, t.VoteTmdb, t.VoteTmdbCount, t.Popularity)).ToList());
        var priorById = priors.ToDictionary(p => p.TitleId);
        var trainingTitles = titles.ToDictionary(
            title => title.Id,
            title => new RecoLevel3TrainingTitle(title.ReleaseYear, title.VoteTmdb, title.Popularity));
        var priorScores = priorById.ToDictionary(entry => entry.Key, entry => entry.Value.Prior);

        var minCoverage = await GetDoubleAsync(SettingsKeys.RecoEmbeddingsMinCoverage, 0.8, cancellationToken);
        var embeddingsEnabled = await GetBoolAsync(SettingsKeys.RecoEmbeddingsEnabled, true, cancellationToken);
        var statePeek = await RecoProfileStates.FindAdminNoTrackingAsync(db, cancellationToken);
        var coverage = statePeek?.EmbeddingCoverage ?? 0;
        var modelReady = string.Equals(statePeek?.ModelStatus, RecoModelStatuses.Ready, StringComparison.OrdinalIgnoreCase);
        var useL2 = embeddingsEnabled && modelReady && coverage >= minCoverage;

        IReadOnlyDictionary<int, float[]> embMap = new Dictionary<int, float[]>();
        if (useL2)
        {
            var modelId = statePeek?.ModelId ?? RecoModelCatalog.DefaultModelId;
            var embRows = await db.TitleEmbeddings.AsNoTracking()
                .Where(e => e.ModelId == modelId && poolIdSet.Contains(e.TitleId))
                .Select(e => new { e.TitleId, e.Vector })
                .ToListAsync(cancellationToken);
            embMap = embRows.ToDictionary(e => e.TitleId, e => RecoEmbeddingService.BytesToFloats(e.Vector));
            if (embMap.Count == 0)
            {
                useL2 = false;
            }
        }

        var n0 = await GetDoubleAsync(SettingsKeys.RecoN0, RecoLearning.DefaultN0, cancellationToken);
        var minRatings = await GetIntAsync(SettingsKeys.RecoMlMinRatings, RecoLevel3.DefaultMinRatings, cancellationToken);

        var explicitSignals = ratings.ToDictionary(
            r => r.TitleId,
            r => RecoSignals.WeightForExplicit(r.Value, RecoSignals.DefaultWeights));

        var unratedPool = poolTitleIds.Where(id => !explicitSignals.ContainsKey(id)).OrderBy(id => id).ToList();
        var positiveIds = ratings.Where(r => RatingValues.IsPositive(r.Value))
            .OrderBy(r => r.TitleId)
            .Select(r => r.TitleId)
            .Take(RecoEvalMetrics.MaxFolds)
            .ToList();
        var dislikeIds = ratings.Where(r => string.Equals(r.Value, RatingValues.Dislike, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.TitleId).Select(r => r.TitleId).Take(RecoEvalMetrics.MaxFolds).ToList();
        var notInterestedIds = ratings.Where(r => string.Equals(
                RatingValues.Normalize(r.Value), RatingValues.NotInterested, StringComparison.Ordinal))
            .OrderBy(r => r.TitleId).Select(r => r.TitleId).Take(RecoEvalMetrics.MaxFolds).ToList();
        var positiveSet = positiveIds.ToHashSet();
        var dislikeSet = dislikeIds.ToHashSet();
        var foldIds = positiveIds.Concat(dislikeIds).Concat(notInterestedIds);

        var rng = new Random(42);
        var priorFolds = new List<(double Auc, bool Hit)>();
        var l1Folds = new List<(double Auc, bool Hit)>();
        var l2Folds = new List<(double Auc, bool Hit)>();
        var combinedFolds = new List<(double Auc, bool Hit)>();
        var l3Folds = new List<(double Auc, bool Hit)>();
        var positiveScores = new List<double>();
        var dislikeScores = new List<double>();
        var notInterestedScores = new List<double>();

        foreach (var heldOutId in foldIds)
        {
            var isLike = positiveSet.Contains(heldOutId);
            var trainSignals = explicitSignals
                .Where(kv => kv.Key != heldOutId)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            var nEff = trainSignals.Values.Sum(Math.Abs);
            var personalWeight = RecoLearning.PersonalWeight(nEff, n0);
            var profile = RecoSparse.BuildProfile(trainSignals, titleVectors);
            var sparseRaw = RecoSparse.ScoreAll(profile, titleVectors);
            var sparseNorm = RecoNormalize.MinMaxByTitle(sparseRaw);

            IReadOnlyDictionary<int, double> knnUnit = new Dictionary<int, double>();
            IReadOnlyDictionary<int, double> knnFeatureScores = new Dictionary<int, double>();
            var foldUseL2 = useL2 && trainSignals.Any(entry => entry.Value != 0 && embMap.ContainsKey(entry.Key));
            if (foldUseL2)
            {
                var knnRaw = RecoKnn.ScoreAll(embMap, trainSignals);
                knnFeatureScores = knnRaw.ToDictionary(kv => kv.Key, kv => RecoKnn.ToUnitInterval(kv.Value));
                knnUnit = RecoNormalize.MinMaxByTitle(knnFeatureScores);
            }

            var weightSparse = !foldUseL2 ? 1.0 : profile.IsEmpty ? 0 : 0.4;
            var weightKnn = 1 - weightSparse;

            IRecoLevel3Scorer? foldL3 = null;
            if (isLike && unratedPool.Count > 0 && trainSignals.Count >= minRatings)
            {
                var examples = RecoLevel3Training.BuildExamples(
                    trainSignals,
                    trainSignals.ToDictionary(entry => entry.Key, entry => entry.Value > 0),
                    trainingTitles,
                    titleVectors,
                    embMap,
                    priorScores,
                    useL2,
                    n0);
                foldL3 = level3Model.TryTrain(
                    examples,
                    minRatings,
                    RecoEvalMetrics.Level3MinPerClass);
            }

            double ScoreLevel(int titleId, string level)
            {
                var prior = priorById[titleId].Prior;
                if (level == "prior" || nEff <= 0 || personalWeight <= 0)
                {
                    return prior;
                }

                var sparse = sparseNorm.GetValueOrDefault(titleId, 0.5);
                var knn = knnUnit.GetValueOrDefault(titleId, 0.5);
                var personal = weightSparse * sparse + weightKnn * knn;
                return level switch
                {
                    "l1" => sparse,
                    "l2" => knn,
                    "combined" => RecoLearning.FuseScore(prior, personal, personalWeight),
                    "l3" when foldL3 is not null => foldL3.Score(BuildFeatures(
                        titleId,
                        titles,
                        titleVectors,
                        sparseRaw.GetValueOrDefault(titleId),
                        knnFeatureScores.GetValueOrDefault(titleId, 0.5),
                        prior,
                        weightSparse,
                        weightKnn,
                        personalWeight,
                        !profile.IsEmpty,
                        foldUseL2 && embMap.ContainsKey(titleId))),
                    _ => RecoLearning.FuseScore(prior, personal, personalWeight),
                };
            }

            if (isLike && unratedPool.Count > 0)
            {
                var negatives = SampleNegatives(unratedPool, heldOutId, RecoEvalMetrics.DefaultNegativesPerFold, rng);

                void Collect(string level, List<(double Auc, bool Hit)> sink)
                {
                    if ((level == "l2" && !foldUseL2) || (level == "l3" && foldL3 is null))
                    {
                        return;
                    }

                    var pos = ScoreLevel(heldOutId, level);
                    var negScores = negatives.Select(id => ScoreLevel(id, level)).ToList();
                    sink.Add((
                        RecoEvalMetrics.PairwiseAuc(pos, negScores),
                        RecoEvalMetrics.HitAt(pos, negScores)));
                }

                Collect("prior", priorFolds);
                Collect("l1", l1Folds);
                Collect("l2", l2Folds);
                Collect("combined", combinedFolds);
                Collect("l3", l3Folds);
            }

            var score = ScoreLevel(heldOutId, "combined");
            (isLike ? positiveScores : dislikeSet.Contains(heldOutId) ? dislikeScores : notInterestedScores).Add(score);
        }

        var priorMetrics = priorFolds.Count > 0 ? RecoEvalMetrics.Aggregate(priorFolds) : null;
        var l1Metrics = l1Folds.Count > 0 ? RecoEvalMetrics.Aggregate(l1Folds) : null;
        var l2Metrics = l2Folds.Count > 0 ? RecoEvalMetrics.Aggregate(l2Folds) : null;
        var combinedMetrics = combinedFolds.Count > 0 ? RecoEvalMetrics.Aggregate(combinedFolds) : null;
        var l3Metrics = l3Folds.Count > 0 ? RecoEvalMetrics.Aggregate(l3Folds) : null;
        var positives = positiveScores.Select(score => (IsLike: true, Score: score));
        var likeVsDislike = RecoEvalMetrics.LikeVsDislikeAuc(
            positives.Concat(dislikeScores.Select(score => (IsLike: false, Score: score))).ToList());
        var likeVsNotInterested = RecoEvalMetrics.LikeVsDislikeAuc(
            positives.Concat(notInterestedScores.Select(score => (IsLike: false, Score: score))).ToList());

        string? skipReason = null;
        var level3Active = RecoEvalMetrics.ShouldActivateLevel3(
            combinedMetrics,
            l3Metrics,
            likeCount,
            dislikeCount,
            minRatings);
        if (!level3Active)
        {
            if (ratingCount < minRatings)
            {
                skipReason = "below_min_ratings";
            }
            else if (likeCount < RecoEvalMetrics.Level3MinPerClass
                     || dislikeCount < RecoEvalMetrics.Level3MinPerClass)
            {
                skipReason = "below_min_per_class";
            }
            else if (combinedMetrics is null)
            {
                skipReason = "insufficient_retrieval_data";
            }
            else if (l3Metrics is null)
            {
                skipReason = "l3_unavailable";
            }
            else if (combinedMetrics is null
                     || l3Metrics.Folds < RecoEvalMetrics.Level3MinEvaluationFolds
                     || l3Metrics.Folds != combinedMetrics.Folds)
            {
                skipReason = "insufficient_l3_folds";
            }
            else
            {
                skipReason = "did_not_beat_combined";
            }
        }

        var report = new RecoEvalReportDto(
            clock.UtcNow,
            ratingCount,
            likeCount,
            dislikeCount,
            poolTitleIds.Count,
            lowSample,
            priorMetrics,
            l1Metrics,
            l2Metrics,
            combinedMetrics,
            l3Metrics,
            likeVsDislike,
            level3Active,
            skipReason,
            new Dictionary<string, double>
            {
                ["a"] = 0.4,
                ["b"] = 0.6,
                ["N0"] = n0,
                ["gamma"] = RecoKnn.DefaultGamma,
                ["K"] = RecoKnn.DefaultK,
                ["m"] = RecoKnn.DefaultSimMin,
            },
            notInterestedCount,
            unratedPool.Count,
            priorFolds.Count > 0 ? Math.Min(unratedPool.Count, RecoEvalMetrics.DefaultNegativesPerFold) : 0,
            positiveScores.Count,
            dislikeScores.Count,
            notInterestedScores.Count,
            likeVsNotInterested);

        await PersistAsync(report, level3Active, cancellationToken);
        return report;
    }

    private async Task PersistAsync(RecoEvalReportDto report, bool level3Active, CancellationToken ct)
    {
        var state = await RecoProfileStates.GetOrCreateAdminAsync(db, ct);
        if (state is null)
        {
            return;
        }

        state.LastEvalJson = JsonSerializer.Serialize(report, JsonOptions);
        state.LastEvalAt = report.EvaluatedAt;
        state.Level3Active = level3Active;
        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyDictionary<int, SparseVector>> BuildTitleVectorsAsync(
        HashSet<int> poolIdSet,
        IReadOnlyList<TitleRow> titles,
        CancellationToken ct)
    {
        var genreRows = await db.TitleGenres.AsNoTracking()
            .Where(tg => poolIdSet.Contains(tg.TitleId))
            .Select(tg => new { tg.TitleId, tg.GenreId })
            .ToListAsync(ct);
        var genresByTitle = genreRows.GroupBy(g => g.TitleId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(x => x.GenreId).ToList());

        var keywordRows = await db.TitleKeywords.AsNoTracking()
            .Where(tk => poolIdSet.Contains(tk.TitleId))
            .Select(tk => new { tk.TitleId, tk.KeywordId })
            .ToListAsync(ct);
        var keywordsByTitle = keywordRows.GroupBy(k => k.TitleId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(x => x.KeywordId).ToList());

        var creditRows = await db.TitleCredits.AsNoTracking()
            .Where(tc => poolIdSet.Contains(tc.TitleId))
            .Select(tc => new { tc.TitleId, tc.PersonId, tc.Role })
            .ToListAsync(ct);
        var castByTitle = creditRows.Where(c => c.Role == CreditRoles.Cast)
            .GroupBy(c => c.TitleId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(x => x.PersonId).Distinct().ToList());
        var directorByTitle = creditRows
            .Where(c => c.Role == CreditRoles.Director || c.Role == CreditRoles.Creator)
            .GroupBy(c => c.TitleId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(x => x.PersonId).Distinct().ToList());

        var sparseFeatures = titles.Select(t => new RecoSparseTitleFeatures(
            t.Id,
            t.Category,
            t.OriginalLanguage,
            t.ReleaseYear,
            genresByTitle.GetValueOrDefault(t.Id) ?? [],
            keywordsByTitle.GetValueOrDefault(t.Id) ?? [],
            castByTitle.GetValueOrDefault(t.Id) ?? [],
            directorByTitle.GetValueOrDefault(t.Id) ?? [],
            ParseCountries(t.OriginCountries))).ToList();

        return RecoSparse.BuildTitleVectors(sparseFeatures);
    }

    private static float[] BuildFeatures(
        int titleId,
        IReadOnlyList<TitleRow> titles,
        IReadOnlyDictionary<int, SparseVector> titleVectors,
        double sparse,
        double knn,
        double prior,
        double weightSparse,
        double weightKnn,
        double personalWeight,
        bool hasSparse,
        bool hasKnn)
    {
        var title = titles.First(t => t.Id == titleId);
        return RecoLevel3.BuildFeatures(
            titleVectors.GetValueOrDefault(titleId),
            sparse,
            knn,
            prior,
            title.ReleaseYear,
            title.VoteTmdb,
            title.Popularity,
            weightSparse,
            weightKnn,
            personalWeight,
            hasSparse,
            hasKnn);
    }

    private static List<int> SampleNegatives(IReadOnlyList<int> unrated, int heldOutId, int count, Random rng)
    {
        var pool = unrated.Where(id => id != heldOutId).ToList();
        if (pool.Count <= count)
        {
            return pool;
        }

        // Deterministic-ish sample without reshuffling the whole list each fold.
        var result = new List<int>(count);
        var step = Math.Max(1, pool.Count / count);
        var offset = rng.Next(step);
        for (var i = 0; i < count && offset + i * step < pool.Count; i++)
        {
            result.Add(pool[offset + i * step]);
        }

        while (result.Count < count)
        {
            result.Add(pool[rng.Next(pool.Count)]);
        }

        return result;
    }

    private static IReadOnlyList<string> ParseCountries(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken ct)
    {
        var raw = await settings.GetRawAsync(key, ct);
        return bool.TryParse(raw?.Trim().Trim('"'), out var v) ? v : fallback;
    }

    private async Task<double> GetDoubleAsync(string key, double fallback, CancellationToken ct)
    {
        var raw = await settings.GetRawAsync(key, ct);
        return double.TryParse(raw?.Trim().Trim('"'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : fallback;
    }

    private async Task<int> GetIntAsync(string key, int fallback, CancellationToken ct)
    {
        var raw = await settings.GetRawAsync(key, ct);
        return int.TryParse(raw?.Trim().Trim('"'), out var v) ? v : fallback;
    }

    private sealed record TitleRow(
        int Id,
        double? VoteTmdb,
        int? VoteTmdbCount,
        double? Popularity,
        string Category,
        string? OriginalLanguage,
        int? ReleaseYear,
        string? OriginCountries);
}
