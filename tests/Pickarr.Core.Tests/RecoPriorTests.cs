using Pickarr.Core.Recommendations;
using Shouldly;

namespace Pickarr.Core.Tests;

public sealed class RecoPriorTests
{
    [Fact]
    public void Compute_orders_by_prior_descending_then_title_id()
    {
        var pool = new[]
        {
            new RecoTitleStats(1, VoteBlended: 5.0, Popularity: 10),
            new RecoTitleStats(2, VoteBlended: 9.0, Popularity: 100),
            new RecoTitleStats(3, VoteBlended: 9.0, Popularity: 100),
            new RecoTitleStats(4, VoteBlended: null, Popularity: null),
        };

        var scored = RecoPrior.Compute(pool);

        scored.Count.ShouldBe(4);
        scored[0].TitleId.ShouldBe(2); // equal prior with 3 → lower TitleId first
        scored[1].TitleId.ShouldBe(3);
        scored[0].Prior.ShouldBe(scored[1].Prior, 1e-12);
        scored.Select(s => s.TitleId).ShouldBe([2, 3, 4, 1]);
        scored[0].Prior.ShouldBeGreaterThan(scored[2].Prior);
        scored[2].Prior.ShouldBeGreaterThan(scored[3].Prior);
    }

    [Fact]
    public void Compute_missing_vote_uses_pool_mean_and_zero_pop()
    {
        var pool = new[]
        {
            new RecoTitleStats(1, VoteBlended: 8.0, Popularity: 50),
            new RecoTitleStats(2, VoteBlended: null, Popularity: null),
        };

        var scored = RecoPrior.Compute(pool);
        var missing = scored.Single(s => s.TitleId == 2);

        missing.Bayes.ShouldBe(8.0, 1e-9); // C = mean of available blended scores
        missing.Pop.ShouldBe(0);
    }

    [Fact]
    public void Compute_keeps_blended_scores_without_a_second_shrink()
    {
        var pool = new[]
        {
            new RecoTitleStats(1, VoteBlended: 9, Popularity: 10),
            new RecoTitleStats(2, VoteBlended: 7, Popularity: 10),
        };

        var scored = RecoPrior.Compute(pool);

        scored.Single(s => s.TitleId == 1).Bayes.ShouldBe(9);
        scored.Single(s => s.TitleId == 2).Bayes.ShouldBe(7);
        RecoPrior.BayesWeight.ShouldBe(0.7);
        RecoPrior.PopWeight.ShouldBe(0.3);
    }

    [Fact]
    public void Compute_without_any_blended_score_uses_zero()
    {
        var pool = new[]
        {
            new RecoTitleStats(1, VoteBlended: null, Popularity: 10),
            new RecoTitleStats(2, VoteBlended: null, Popularity: null),
        };

        RecoPrior.Compute(pool).ShouldAllBe(score => score.Bayes == 0);
    }

    [Fact]
    public void Compute_is_deterministic_on_same_input()
    {
        var pool = new[]
        {
            new RecoTitleStats(5, 7.2, 12.5),
            new RecoTitleStats(1, 6.1, 3.0),
            new RecoTitleStats(9, null, 1.0),
        };

        var a = RecoPrior.Compute(pool).Select(s => (s.TitleId, Math.Round(s.Prior, 10))).ToList();
        var b = RecoPrior.Compute(pool).Select(s => (s.TitleId, Math.Round(s.Prior, 10))).ToList();
        a.ShouldBe(b);
    }

    [Fact]
    public void Stage_and_weight_follow_thresholds()
    {
        RecoLearning.StageFromEffectiveN(0).ShouldBe(RecoLearning.StageCold);
        RecoLearning.StageFromEffectiveN(4.9).ShouldBe(RecoLearning.StageCold);
        RecoLearning.StageFromEffectiveN(5).ShouldBe(RecoLearning.StageWarming);
        RecoLearning.StageFromEffectiveN(30).ShouldBe(RecoLearning.StagePersonalized);

        RecoLearning.PersonalWeight(0).ShouldBe(0);
        RecoLearning.PersonalWeight(15).ShouldBe(0.5);
        RecoLearning.PersonalWeight(30).ShouldBe(0.9); // 1 - residual 0.1
        RecoLearning.PersonalWeight(100).ShouldBe(0.9);
    }
}
