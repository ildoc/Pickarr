using Pickarr.Core.Recommendations;

namespace Pickarr.Core.Abstractions;

/// <summary>Trains and scores the optional supervised level-3 model (ML.NET in Infrastructure).</summary>
public interface IRecoLevel3Model
{
    /// <summary>Returns null when examples, classes, or feature vectors do not meet the training requirements.</summary>
    IRecoLevel3Scorer? TryTrain(IReadOnlyList<RecoLevel3Example> examples, int minRatings, int minPerClass);
}

public interface IRecoLevel3Scorer
{
    /// <summary>Probability of Like in [0,1].</summary>
    double Score(float[] features);
}
