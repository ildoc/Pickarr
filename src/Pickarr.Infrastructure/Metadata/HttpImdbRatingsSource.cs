namespace Pickarr.Infrastructure.Metadata;

public interface IImdbRatingsSource
{
    Task<Stream> OpenAsync(CancellationToken cancellationToken = default);
}

public sealed class HttpImdbRatingsSource(HttpClient http) : IImdbRatingsSource
{
    public const string DatasetUrl = "https://datasets.imdbws.com/title.ratings.tsv.gz";

    public async Task<Stream> OpenAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(
            DatasetUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new MemoryStream(bytes, writable: false);
    }
}
