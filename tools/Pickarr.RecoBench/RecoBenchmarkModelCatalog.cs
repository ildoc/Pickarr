namespace Pickarr.RecoBench;

public sealed record RecoBenchmarkModelDescriptor(
    string ModelId,
    string Revision,
    string OnnxPath,
    string OnnxSha256,
    string TokenizerPath,
    string TokenizerSha256,
    int EmbeddingDimensions,
    int MaxTokens,
    string Pooling,
    string ModelCardUrl,
    int StartTokenId,
    int EndTokenId);

public static class RecoBenchmarkModelCatalog
{
    public const string CurrentRevision = "751bff37182d3f1213fa05d7196b954e230abad9";
    public const string MultilingualRevision = "2c4055b12046f11709e9df2c122e59ffbdc2f900";

    public static RecoBenchmarkModelDescriptor Current { get; } = new(
        "all-MiniLM-L6-v2@q8",
        CurrentRevision,
        "model.onnx",
        Pickarr.Core.Recommendations.RecoModelCatalog.DefaultOnnxSha256,
        "vocab.txt",
        "07ECED375CEC144D27C900241F3E339478DEC958F92FDDBC551F295C992038A3",
        384,
        256,
        "mean_pool_all_tokens_l2",
        "https://huggingface.co/Xenova/all-MiniLM-L6-v2",
        101,
        102);

    public static RecoBenchmarkModelDescriptor Multilingual { get; } = new(
        "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2",
        MultilingualRevision,
        Path.Combine("onnx", "model_quantized.onnx"),
        "66FC00F5F29AFCAFF34092E1BDD20008CA3918265A82FB9695A551E510CC4EBC",
        "tokenizer.json",
        "B60B6B43406A48BF3638526314F3D232D97058BC93472FF2DE930D43686FA441",
        384,
        128,
        "attention_masked_mean_l2",
        "https://huggingface.co/sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2",
        0,
        2);

    public static IReadOnlyList<(string Language, string Text)> SelfTestTexts { get; } =
    [
        ("en", "A science fiction thriller about time travel."),
        ("it", "Un thriller di fantascienza sui viaggi nel tempo."),
        ("fr", "Un thriller de science-fiction sur le voyage dans le temps."),
        ("es", "Un thriller de ciencia ficcion sobre viajes en el tiempo."),
        ("ja", "タイムトラベルを題材にしたSFスリラー。"),
        ("ar", "فيلم خيال علمي وإثارة عن السفر عبر الزمن."),
    ];

    public static IReadOnlyDictionary<string, string> MultilingualTokenGoldens { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["en"] = "DC77C90D28BFA73D26D852358B0EDE7AD277FADF466FA3DE2697C9262DC89887",
            ["it"] = "38E88B19918B9C4EE76758F6AD0F1403E120773645F94F3372CCCBD94C254E8C",
            ["fr"] = "89E16FDE8BFCD2ED2FC0F9D979ABF87269BACC77651804427229689B54E21E0E",
            ["es"] = "8681698CB2C6D8466316549AD749D058E6184CFC7A828F9AC6E98FC3549C2A5C",
            ["ja"] = "FD2F29154EE2946840FB35ABD87B5DD2FFB46220221754E4543CCDF8F61506CE",
            ["ar"] = "AEF4506DFE4765EF2359070BA3BE8591B100C954199BAB4034605B207534A53C",
        };
}
