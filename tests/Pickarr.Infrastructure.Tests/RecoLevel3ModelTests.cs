using Pickarr.Core.Recommendations;
using Pickarr.Infrastructure.Metadata.Reco;
using Shouldly;

namespace Pickarr.Infrastructure.Tests;

public sealed class RecoLevel3ModelTests
{
    [Fact]
    public void TryTrain_returns_null_under_min_ratings()
    {
        var model = new RecoLevel3Model();
        var examples = Enumerable.Range(0, 40)
            .Select(i => new RecoLevel3Example(
                i,
                i % 2 == 0,
                RecoLevel3.BuildFeatures(null, 0.4, 0.5, 0.5, 2000 + i, 6, 10)))
            .ToList();

        model.TryTrain(examples, minRatings: 100, minPerClass: 20).ShouldBeNull();
    }

    [Fact]
    public void TryTrain_scores_when_thresholds_met()
    {
        var model = new RecoLevel3Model();
        var examples = new List<RecoLevel3Example>();
        for (var i = 0; i < 60; i++)
        {
            // Separable: likes have high sparse, dislikes low.
            examples.Add(new RecoLevel3Example(
                i,
                IsLike: true,
                RecoLevel3.BuildFeatures(null, sparse: 0.9, knn: 0.8, prior: 0.6, 2015, 8, 30)));
            examples.Add(new RecoLevel3Example(
                1000 + i,
                IsLike: false,
                RecoLevel3.BuildFeatures(null, sparse: 0.1, knn: 0.2, prior: 0.4, 1980, 4, 2)));
        }

        var scorer = model.TryTrain(examples, minRatings: 100, minPerClass: 20);
        scorer.ShouldNotBeNull();

        var likeScore = scorer!.Score(RecoLevel3.BuildFeatures(null, 0.9, 0.8, 0.6, 2015, 8, 30));
        var dislikeScore = scorer.Score(RecoLevel3.BuildFeatures(null, 0.1, 0.2, 0.4, 1980, 4, 2));
        likeScore.ShouldBeGreaterThan(dislikeScore);
    }

    [Fact]
    public void Training_features_exclude_the_target_signal()
    {
        var titleFeatures = Enumerable.Range(1, 3)
            .Select(id => new RecoSparseTitleFeatures(
                id,
                "movie",
                "en",
                2000 + id,
                [id],
                [],
                [],
                [],
                []))
            .ToList();
        var vectors = RecoSparse.BuildTitleVectors(titleFeatures);
        var labels = new Dictionary<int, bool> { [1] = true, [2] = false, [3] = true };
        var titles = Enumerable.Range(1, 3)
            .ToDictionary(id => id, _ => new RecoLevel3TrainingTitle(2000, 6, 10));
        var priors = Enumerable.Range(1, 3).ToDictionary(id => id, _ => 0.5);
        var signals = new Dictionary<int, double> { [1] = 1, [2] = -1, [3] = 0.5 };

        var before = RecoLevel3Training.BuildExamples(
            signals, labels, titles, vectors, new Dictionary<int, float[]>(), priors, false, 30)
            .Single(example => example.TitleId == 1);

        signals[1] = -1;
        labels[1] = false;
        var after = RecoLevel3Training.BuildExamples(
            signals, labels, titles, vectors, new Dictionary<int, float[]>(), priors, false, 30)
            .Single(example => example.TitleId == 1);

        after.Features.ShouldBe(before.Features);
    }

    [Fact]
    public void Cross_fit_knn_uses_training_neighbours_but_never_the_target_vote()
    {
        var titles = new Dictionary<int, RecoLevel3TrainingTitle> { [1] = new(2000, 6, 10), [2] = new(2000, 6, 10) };
        var labels = new Dictionary<int, bool> { [1] = true, [2] = true };
        var priors = titles.Keys.ToDictionary(id => id, _ => 0.5);
        var embeddings = titles.Keys.ToDictionary(id => id, _ => new float[] { 1, 0 });
        var signals = new Dictionary<int, double> { [1] = 1, [2] = 1 };
        float[] TargetFeatures() => RecoLevel3Training.BuildExamples(signals, labels, titles,
            new Dictionary<int, SparseVector>(), embeddings, priors, true, 30).Single(e => e.TitleId == 1).Features;

        var before = TargetFeatures();
        before[RecoLevel3.KnnFeatureIndex].ShouldBe(0.55f);
        signals[1] = -1;
        labels[1] = false;
        TargetFeatures().ShouldBe(before);
        signals[2] = -1;
        labels[2] = false;
        TargetFeatures()[RecoLevel3.KnnFeatureIndex].ShouldBe(0.45f);
        embeddings.Remove(2);
        var unavailable = TargetFeatures();
        unavailable[RecoLevel3.KnnFeatureIndex].ShouldBe(0.5f);
        unavailable[RecoLevel3.KnnAvailableFeatureIndex].ShouldBe(0f);
    }

    [Fact]
    public void TryTrain_returns_null_for_invalid_feature_dimensions()
    {
        var examples = Enumerable.Range(0, 40)
            .Select(i => new RecoLevel3Example(
                i,
                i % 2 == 0,
                RecoLevel3.BuildFeatures(null, 0.4, 0.5, 0.5, 2000, 6, 10)))
            .ToList();
        examples[0] = examples[0] with { Features = [0f] };

        new RecoLevel3Model().TryTrain(examples, minRatings: 40, minPerClass: 20).ShouldBeNull();
    }
}
