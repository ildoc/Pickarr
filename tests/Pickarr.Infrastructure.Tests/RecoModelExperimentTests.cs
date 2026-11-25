using System.Diagnostics;
using Pickarr.Core.Recommendations;
using Pickarr.RecoBench;
using Shouldly;

namespace Pickarr.Infrastructure.Tests;

public sealed class RecoModelExperimentTests
{
    [Fact]
    public void Model_descriptors_pin_artifact_identity_and_model_specific_sequence_contracts()
    {
        var current = RecoBenchmarkModelCatalog.Current;
        var multilingual = RecoBenchmarkModelCatalog.Multilingual;

        current.ModelId.ShouldBe(RecoModelCatalog.DefaultModelId);
        current.Revision.ShouldBe("751bff37182d3f1213fa05d7196b954e230abad9");
        current.OnnxSha256.ShouldBe(RecoModelCatalog.DefaultOnnxSha256);
        current.TokenizerSha256.ShouldBe("07ECED375CEC144D27C900241F3E339478DEC958F92FDDBC551F295C992038A3");
        current.EmbeddingDimensions.ShouldBe(384);
        current.MaxTokens.ShouldBe(256);
        current.StartTokenId.ShouldBe(101);
        current.EndTokenId.ShouldBe(102);
        multilingual.ModelId.ShouldBe("sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2");
        multilingual.Revision.ShouldBe("2c4055b12046f11709e9df2c122e59ffbdc2f900");
        multilingual.OnnxSha256.ShouldBe("66FC00F5F29AFCAFF34092E1BDD20008CA3918265A82FB9695A551E510CC4EBC");
        multilingual.TokenizerSha256.ShouldBe("B60B6B43406A48BF3638526314F3D232D97058BC93472FF2DE930D43686FA441");
        multilingual.EmbeddingDimensions.ShouldBe(384);
        multilingual.MaxTokens.ShouldBe(128);
        multilingual.StartTokenId.ShouldBe(0);
        multilingual.EndTokenId.ShouldBe(2);
        multilingual.Pooling.ShouldBe("attention_masked_mean_l2");
        multilingual.ModelCardUrl.ShouldContain("paraphrase-multilingual-MiniLM-L12-v2");
    }

    [Fact]
    public void Truncation_keeps_model_specific_end_token_and_consistent_attention_inputs()
    {
        var (ids, typeIds, mask) = MultilingualMiniLmEmbedder.TruncateWithEndToken(
            [0, 10, 11, 12, 2],
            [0, 0, 0, 0, 0],
            [1, 1, 1, 1, 1],
            maxTokens: 4,
            endTokenId: 2);

        ids.ShouldBe([0, 10, 11, 2]);
        typeIds.ShouldBe([0, 0, 0, 0]);
        mask.ShouldBe([1, 1, 1, 1]);
    }

    [Fact]
    public void Mean_pooling_uses_attention_mask_and_returns_unit_vector()
    {
        var embedding = MultilingualMiniLmEmbedder.MeanPoolAndNormalize(
            [3, 0, 100, 100, 1, 0],
            [1, 1, 0],
            dimensions: 2);

        embedding.Length.ShouldBe(2);
        ((double)embedding[0]).ShouldBe(103 / Math.Sqrt(20609), 1e-5);
        ((double)embedding[1]).ShouldBe(100 / Math.Sqrt(20609), 1e-5);
        Math.Sqrt(embedding.Sum(value => value * value)).ShouldBe(1, 1e-5);
    }

    [Fact]
    public void Mean_pooling_rejects_an_empty_attention_mask()
    {
        Should.Throw<InvalidDataException>(() =>
            MultilingualMiniLmEmbedder.MeanPoolAndNormalize([0, 0], [0], dimensions: 2));
    }

    [Fact]
    public void Model_self_test_reports_missing_local_artifacts_without_echoing_paths()
    {
        var current = Path.Combine(Path.GetTempPath(), $"missing-current-{Guid.NewGuid():N}");
        var multilingual = Path.Combine(Path.GetTempPath(), $"missing-multilingual-{Guid.NewGuid():N}");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(RecoModelSelfTestRunner).Assembly.Location);
        start.ArgumentList.Add("model-self-test");
        start.ArgumentList.Add(current);
        start.ArgumentList.Add(multilingual);

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.ShouldBe(1);
        stderr.ShouldContain("required model artifact is missing");
        stdout.ShouldBeEmpty();
        stderr.ShouldNotContain(current);
        stderr.ShouldNotContain(multilingual);
    }
}
