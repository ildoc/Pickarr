using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Pickarr.Core.Recommendations;
using Tokenizers.HuggingFace.Tokenizer;

namespace Pickarr.RecoBench;

public sealed class MultilingualMiniLmEmbedder : IDisposable
{
    private readonly object _gate = new();
    private readonly InferenceSession _session;
    private readonly Tokenizer _tokenizer;
    private readonly string _outputName;
    private readonly int _endTokenId;

    public string OnnxSha256 { get; }
    public string TokenizerSha256 { get; }

    public MultilingualMiniLmEmbedder(string modelDirectory, int threads = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        if (threads <= 0)
            throw new ArgumentOutOfRangeException(nameof(threads));

        var descriptor = RecoBenchmarkModelCatalog.Multilingual;
        var onnxPath = Path.Combine(modelDirectory, descriptor.OnnxPath);
        var tokenizerPath = Path.Combine(modelDirectory, descriptor.TokenizerPath);
        OnnxSha256 = VerifyArtifact(onnxPath, descriptor.OnnxSha256, "model_quantized.onnx");
        TokenizerSha256 = VerifyArtifact(tokenizerPath, descriptor.TokenizerSha256, "tokenizer.json");

        var artifacts = LoadArtifacts(onnxPath, tokenizerPath, threads, descriptor);
        _session = artifacts.Session;
        _outputName = artifacts.OutputName;
        _tokenizer = artifacts.Tokenizer;
        _endTokenId = artifacts.EndTokenId;
    }

    public float[] Embed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
        {
            var (ids, typeIds, attentionMask) = Encode_NoLock(text);

            var length = ids.Length;
            if (length == 0 || typeIds.Length != length || attentionMask.Length != length)
                throw new InvalidDataException("Multilingual tokenizer returned inconsistent model inputs.");

            var inputIdsTensor = new DenseTensor<long>([1, length]);
            var typeIdsTensor = new DenseTensor<long>([1, length]);
            var attentionTensor = new DenseTensor<long>([1, length]);
            for (var index = 0; index < length; index++)
            {
                inputIdsTensor[0, index] = ids[index];
                typeIdsTensor[0, index] = typeIds[index];
                attentionTensor[0, index] = attentionMask[index];
            }

            using var results = _session.Run(
            [
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("token_type_ids", typeIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionTensor),
            ]);
            var hidden = results.Single(result => result.Name == _outputName).AsEnumerable<float>().ToArray();
            if (hidden.Length != length * RecoBenchmarkModelCatalog.Multilingual.EmbeddingDimensions)
                throw new InvalidDataException("Multilingual ONNX output has an unexpected shape.");

            return MeanPoolAndNormalize(
                hidden,
                attentionMask,
                RecoBenchmarkModelCatalog.Multilingual.EmbeddingDimensions);
        }
    }

    internal int[] Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
            return Encode_NoLock(text).Ids;
    }

    private (int[] Ids, int[] TypeIds, int[] AttentionMask) Encode_NoLock(string text)
    {
        var encoded = _tokenizer.Encode(
            text,
            addSpecialTokens: true,
            includeTypeIds: true,
            includeAttentionMask: true).First();
        return TruncateWithEndToken(
            encoded.Ids.Select(id => checked((int)id)).ToArray(),
            encoded.TypeIds.Select(id => checked((int)id)).ToArray(),
            encoded.AttentionMask.Select(id => checked((int)id)).ToArray(),
            RecoBenchmarkModelCatalog.Multilingual.MaxTokens,
            _endTokenId);
    }

    public static (int[] Ids, int[] TypeIds, int[] AttentionMask) TruncateWithEndToken(
        int[] ids,
        int[] typeIds,
        int[] attentionMask,
        int maxTokens,
        int endTokenId)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(typeIds);
        ArgumentNullException.ThrowIfNull(attentionMask);
        if (maxTokens < 2 || ids.Length != typeIds.Length || ids.Length != attentionMask.Length)
            throw new ArgumentException("Tokenizer sequence inputs are invalid.");
        if (ids.Length <= maxTokens)
            return (ids, typeIds, attentionMask);

        var truncatedIds = ids[..maxTokens];
        var truncatedTypeIds = typeIds[..maxTokens];
        var truncatedAttentionMask = attentionMask[..maxTokens];
        truncatedIds[^1] = endTokenId;
        truncatedTypeIds[^1] = 0;
        truncatedAttentionMask[^1] = 1;
        return (truncatedIds, truncatedTypeIds, truncatedAttentionMask);
    }

    public static float[] MeanPoolAndNormalize(
        IReadOnlyList<float> hidden,
        IReadOnlyList<int> attentionMask,
        int dimensions)
    {
        ArgumentNullException.ThrowIfNull(hidden);
        ArgumentNullException.ThrowIfNull(attentionMask);
        if (dimensions <= 0 || attentionMask.Count == 0 || hidden.Count != attentionMask.Count * dimensions)
            throw new ArgumentException("Hidden state and attention mask dimensions do not match.");

        var pooled = new float[dimensions];
        var tokenCount = 0;
        for (var token = 0; token < attentionMask.Count; token++)
        {
            var mask = attentionMask[token];
            if (mask is not (0 or 1))
                throw new ArgumentException("Attention mask values must be zero or one.");
            if (mask == 0)
                continue;

            tokenCount++;
            var offset = token * dimensions;
            for (var dimension = 0; dimension < dimensions; dimension++)
            {
                var value = hidden[offset + dimension];
                if (!float.IsFinite(value))
                    throw new InvalidDataException("Multilingual ONNX output contains a non-finite value.");
                pooled[dimension] += value;
            }
        }

        if (tokenCount == 0)
            throw new InvalidDataException("Multilingual tokenizer produced an empty attention mask.");
        for (var dimension = 0; dimension < dimensions; dimension++)
            pooled[dimension] /= tokenCount;
        RecoKnn.L2Normalize(pooled);
        var norm = Math.Sqrt(pooled.Sum(value => (double)value * value));
        if (!double.IsFinite(norm) || Math.Abs(norm - 1) > 0.001)
            throw new InvalidDataException("Multilingual ONNX output cannot be normalized.");
        return pooled;
    }

    private static string VerifyArtifact(string path, string expectedSha256, string artifactName)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required model artifact is missing: {artifactName}.");

        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Model artifact SHA-256 mismatch: {artifactName}.");
        return actual;
    }

    private static void ValidateInputs(IReadOnlyDictionary<string, NodeMetadata> inputs)
    {
        string[] names = ["input_ids", "attention_mask", "token_type_ids"];
        if (inputs.Count != names.Length || names.Any(name => !inputs.ContainsKey(name)))
            throw new InvalidDataException("Multilingual ONNX inputs do not match the verified model signature.");
        foreach (var name in names)
        {
            var metadata = inputs[name];
            if (metadata.ElementType != typeof(long) || metadata.Dimensions.Length != 2)
                throw new InvalidDataException($"Multilingual ONNX input has an unexpected type or rank: {name}.");
        }
    }

    private static string FindHiddenStateOutput(IReadOnlyDictionary<string, NodeMetadata> outputs)
    {
        var match = outputs.Where(output =>
                output.Value.ElementType == typeof(float)
                && output.Value.Dimensions.Length == 3
                && output.Value.Dimensions[^1] == RecoBenchmarkModelCatalog.Multilingual.EmbeddingDimensions)
            .Select(output => output.Key)
            .ToArray();
        return match.Length == 1
            ? match[0]
            : throw new InvalidDataException("Multilingual ONNX model must expose one token hidden-state output.");
    }

    private static (InferenceSession Session, string OutputName, Tokenizer Tokenizer, int EndTokenId) LoadArtifacts(
        string onnxPath,
        string tokenizerPath,
        int threads,
        RecoBenchmarkModelDescriptor descriptor)
    {
        Tokenizer? tokenizer = null;
        InferenceSession? session = null;
        var loaded = false;
        try
        {
            tokenizer = Tokenizer.FromFile(tokenizerPath);
            var empty = tokenizer.Encode("", addSpecialTokens: true).First().Ids;
            if (empty.Count != 2
                || empty[0] != descriptor.StartTokenId
                || empty[1] != descriptor.EndTokenId)
                throw new InvalidDataException("Multilingual tokenizer special-token IDs do not match the pinned model.");

            using var options = new SessionOptions
            {
                IntraOpNumThreads = threads,
                InterOpNumThreads = 1,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            };
            session = new InferenceSession(onnxPath, options);
            ValidateInputs(session.InputMetadata);
            var outputName = FindHiddenStateOutput(session.OutputMetadata);
            loaded = true;
            return (session, outputName, tokenizer, descriptor.EndTokenId);
        }
        finally
        {
            if (!loaded)
            {
                tokenizer?.Dispose();
                session?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _tokenizer.Dispose();
            _session.Dispose();
        }
    }
}
