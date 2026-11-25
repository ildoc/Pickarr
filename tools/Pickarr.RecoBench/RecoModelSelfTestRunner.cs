using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pickarr.Core.Recommendations;
using Pickarr.Infrastructure.Metadata.Reco;

namespace Pickarr.RecoBench;

public static class RecoModelSelfTestRunner
{
    private const double MinimumRepeatCosine = 0.999;

    public static string Run(string currentModelDirectory, string multilingualModelDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentModelDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(multilingualModelDirectory);

        var current = RecoBenchmarkModelCatalog.Current;
        var currentOnnxHash = VerifyFile(
            Path.Combine(currentModelDirectory, current.OnnxPath),
            current.OnnxSha256,
            "model.onnx");
        var currentTokenizerHash = HashFile(
            Path.Combine(currentModelDirectory, current.TokenizerPath),
            "vocab.txt");
        if (!string.Equals(currentTokenizerHash, current.TokenizerSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Model artifact SHA-256 mismatch: vocab.txt.");

        using var currentEmbedder = new RecoOnnxEmbedder();
        if (!currentEmbedder.TryLoad(
                currentModelDirectory,
                RecoModelCatalog.DefaultModelId,
                threads: 1,
                NullLogger.Instance))
        {
            throw new InvalidDataException("The current model could not be loaded.");
        }

        using var multilingualEmbedder = new MultilingualMiniLmEmbedder(multilingualModelDirectory);
        var checks = new List<ModelTextCheck>();
        var tokenChecks = new List<ModelTokenizerCheck>();
        foreach (var (language, text) in RecoBenchmarkModelCatalog.SelfTestTexts)
        {
            var tokenIds = multilingualEmbedder.Tokenize(text);
            var tokenHash = TokenHash(tokenIds);
            if (!RecoBenchmarkModelCatalog.MultilingualTokenGoldens.TryGetValue(language, out var expectedTokenHash)
                || !string.Equals(tokenHash, expectedTokenHash, StringComparison.Ordinal))
                throw new InvalidDataException($"Multilingual tokenizer golden mismatch for language {language}.");
            var currentVector = currentEmbedder.Embed(text);
            var multilingualVector = multilingualEmbedder.Embed(text);
            ValidateVector(currentVector, current.EmbeddingDimensions, "current");
            ValidateVector(multilingualVector, RecoBenchmarkModelCatalog.Multilingual.EmbeddingDimensions, "multilingual");
            checks.Add(new ModelTextCheck(language, currentVector.Length, multilingualVector.Length));
            tokenChecks.Add(new ModelTokenizerCheck(language, tokenIds.Length, tokenHash));
        }

        var repeated = multilingualEmbedder.Embed(RecoBenchmarkModelCatalog.SelfTestTexts[0].Text);
        var first = multilingualEmbedder.Embed(RecoBenchmarkModelCatalog.SelfTestTexts[0].Text);
        ValidateVector(repeated, RecoBenchmarkModelCatalog.Multilingual.EmbeddingDimensions, "multilingual");
        var repeatCosine = RecoKnn.Dot(repeated, first);
        if (repeatCosine < MinimumRepeatCosine)
            throw new InvalidDataException("Multilingual self-test inference is not repeatable.");

        var report = new ModelSelfTestReport(
            "embedding_model_self_test",
            new ModelArtifactIdentity(
                current.ModelId,
                current.Revision,
                currentOnnxHash,
                currentTokenizerHash,
                current.EmbeddingDimensions,
                current.MaxTokens,
                current.Pooling,
                current.ModelCardUrl,
                current.StartTokenId,
                current.EndTokenId),
            new ModelArtifactIdentity(
                RecoBenchmarkModelCatalog.Multilingual.ModelId,
                RecoBenchmarkModelCatalog.Multilingual.Revision,
                multilingualEmbedder.OnnxSha256,
                multilingualEmbedder.TokenizerSha256,
                RecoBenchmarkModelCatalog.Multilingual.EmbeddingDimensions,
                RecoBenchmarkModelCatalog.Multilingual.MaxTokens,
                RecoBenchmarkModelCatalog.Multilingual.Pooling,
                RecoBenchmarkModelCatalog.Multilingual.ModelCardUrl,
                RecoBenchmarkModelCatalog.Multilingual.StartTokenId,
                RecoBenchmarkModelCatalog.Multilingual.EndTokenId),
            tokenChecks,
            checks,
            repeatCosine,
            "model-specific embeddings remain separate; no vectors or input text are serialized");
        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string TokenHash(IReadOnlyList<int> ids) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture))))));

    private static void ValidateVector(float[] vector, int dimensions, string model)
    {
        if (vector.Length != dimensions || vector.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException($"The {model} model returned an invalid embedding.");
        var norm = Math.Sqrt(vector.Sum(value => (double)value * value));
        if (!double.IsFinite(norm) || Math.Abs(norm - 1) > 0.001)
            throw new InvalidDataException($"The {model} model returned a non-unit embedding.");
    }

    private static string VerifyFile(string path, string expectedHash, string artifact)
    {
        var actual = HashFile(path, artifact);
        if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Model artifact SHA-256 mismatch: {artifact}.");
        return actual;
    }

    private static string HashFile(string path, string artifact)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required model artifact is missing: {artifact}.");
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record ModelArtifactIdentity(
        string ModelId,
        string Revision,
        string OnnxSha256,
        string TokenizerSha256,
        int Dimensions,
        int MaxTokens,
        string Pooling,
        string ModelCardUrl,
        int StartTokenId,
        int EndTokenId);

    private sealed record ModelTextCheck(string Language, int CurrentDimensions, int MultilingualDimensions);
    private sealed record ModelTokenizerCheck(string Language, int TokenCount, string TokenIdsSha256);

    private sealed record ModelSelfTestReport(
        string Kind,
        ModelArtifactIdentity CurrentModel,
        ModelArtifactIdentity MultilingualModel,
        IReadOnlyList<ModelTokenizerCheck> TokenizerChecks,
        IReadOnlyList<ModelTextCheck> TextChecks,
        double MultilingualRepeatCosine,
        string Limitation);
}
