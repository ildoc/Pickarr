using Pickarr.Core.Recommendations;

namespace Pickarr.Infrastructure.Metadata.Reco;

public sealed record RecoLevel3TrainingTitle(
    int? ReleaseYear,
    double? VoteTmdb,
    double? Popularity);

public static class RecoLevel3Training
{
    public const int CrossFitFoldCount = 5;

    public static List<RecoLevel3Example> BuildExamples(
        IReadOnlyDictionary<int, double> signals,
        IReadOnlyDictionary<int, bool> labels,
        IReadOnlyDictionary<int, RecoLevel3TrainingTitle> titles,
        IReadOnlyDictionary<int, SparseVector> titleVectors,
        IReadOnlyDictionary<int, float[]> embeddings,
        IReadOnlyDictionary<int, double> priors,
        bool embeddingsAvailable,
        double n0)
    {
        var trainingIds = labels.Keys
            .Where(id => titles.ContainsKey(id) && priors.ContainsKey(id))
            .Order()
            .ToList();
        var foldCount = Math.Min(CrossFitFoldCount, trainingIds.Count);
        if (foldCount < 2)
        {
            return [];
        }

        var random = new Random(42);
        for (var i = trainingIds.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (trainingIds[i], trainingIds[j]) = (trainingIds[j], trainingIds[i]);
        }

        var examples = new List<RecoLevel3Example>(trainingIds.Count);
        for (var fold = 0; fold < foldCount; fold++)
        {
            var heldOutIds = trainingIds
                .Where((_, index) => index % foldCount == fold)
                .ToList();
            var heldOutIdSet = heldOutIds.ToHashSet();
            var foldSignals = signals
                .Where(entry => !heldOutIdSet.Contains(entry.Key))
                .ToDictionary(entry => entry.Key, entry => entry.Value);

            var profile = RecoSparse.BuildProfile(foldSignals, titleVectors);
            var sparseScores = heldOutIds.ToDictionary(
                titleId => titleId,
                titleId => titleVectors.TryGetValue(titleId, out var vector) ? profile.Dot(vector) : 0);
            var effectiveN = foldSignals.Values.Sum(Math.Abs);
            var foldUseKnn = embeddingsAvailable && foldSignals.Any(entry => entry.Value != 0 && embeddings.ContainsKey(entry.Key));
            IReadOnlyDictionary<int, double> knnScores = new Dictionary<int, double>();
            if (foldUseKnn)
            {
                var heldOutEmbeddings = heldOutIds
                    .Where(embeddings.ContainsKey)
                    .ToDictionary(titleId => titleId, titleId => embeddings[titleId]);
                var raw = RecoKnn.ScoreCandidates(heldOutEmbeddings, embeddings, foldSignals);
                knnScores = raw.ToDictionary(entry => entry.Key, entry => RecoKnn.ToUnitInterval(entry.Value));
            }

            var weightSparse = !foldUseKnn ? 1.0 : profile.IsEmpty ? 0 : 0.4;
            var weightKnn = 1 - weightSparse;
            var personalWeight = RecoLearning.PersonalWeight(effectiveN, n0);
            foreach (var titleId in heldOutIds)
            {
                var title = titles[titleId];
                var hasKnn = foldUseKnn && embeddings.ContainsKey(titleId);
                examples.Add(new RecoLevel3Example(
                    titleId,
                    labels[titleId],
                    RecoLevel3.BuildFeatures(
                        titleVectors.GetValueOrDefault(titleId),
                        sparseScores.GetValueOrDefault(titleId),
                        knnScores.GetValueOrDefault(titleId, 0.5),
                        priors[titleId],
                        title.ReleaseYear,
                        title.VoteTmdb,
                        title.Popularity,
                        weightSparse,
                        weightKnn,
                        personalWeight,
                        hasSparse: !profile.IsEmpty,
                        hasKnn: hasKnn)));
            }
        }

        return examples;
    }
}
