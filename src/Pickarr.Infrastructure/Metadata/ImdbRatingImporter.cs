using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Domain.Entities;
using Pickarr.Core.Metadata;
using Pickarr.Infrastructure.Persistence;

namespace Pickarr.Infrastructure.Metadata;

public interface IImdbRatingImporter
{
    Task<ImdbRatingImportResult> ImportAsync(Stream gzipStream, CancellationToken cancellationToken = default);

    Task ApplyStoredRatingAsync(Title title, CancellationToken cancellationToken = default);
}

public sealed record ImdbRatingImportResult(
    double MeanVote,
    AudiencePrior Prior,
    AudiencePrior? PreviousPrior,
    int RowCount,
    int TitlesUpdated);

public sealed class ImdbRatingImportException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class ImdbRatingImporter(PickarrDbContext db, IClock clock) : IImdbRatingImporter
{
    private const int InsertBatchSize = 2000;
    private const int TitleBatchSize = 200;

    public async Task<ImdbRatingImportResult> ImportAsync(
        Stream gzipStream,
        CancellationToken cancellationToken = default)
    {
        var parsed = ImdbRatingsFile.Parse(gzipStream);
        var previous = await db.ImdbRatingSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == ImdbRatingSnapshot.SingletonId, cancellationToken);
        var prior = AudienceScore.Calibrate(
            await LoadCatalogSamplesAsync(parsed, cancellationToken),
            parsed.MeanVote);

        await ReplaceDumpAsync(parsed, prior, cancellationToken);
        var titlesUpdated = await ApplyToTitlesAsync(parsed, prior, cancellationToken);
        return new ImdbRatingImportResult(
            parsed.MeanVote,
            prior,
            previous is null ? null : new AudiencePrior(previous.PriorMean, previous.TmdbOffset),
            parsed.Rows.Count,
            titlesUpdated);
    }

    public async Task ApplyStoredRatingAsync(Title title, CancellationToken cancellationToken = default)
    {
        ImdbRatingRow? row = null;
        if (!string.IsNullOrWhiteSpace(title.ImdbId))
        {
            var stored = await db.ImdbTitleRatings.AsNoTracking()
                .SingleOrDefaultAsync(rating => rating.Tconst == title.ImdbId, cancellationToken);
            if (stored is not null)
            {
                row = new ImdbRatingRow(stored.Tconst, stored.AverageRating, stored.NumVotes);
            }
        }

        ApplyStoredRating(title, row, await LoadPriorAsync(title, cancellationToken));
    }

    private async Task<AudiencePrior> LoadPriorAsync(Title title, CancellationToken cancellationToken)
    {
        var snapshot = await db.ImdbRatingSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == ImdbRatingSnapshot.SingletonId, cancellationToken);
        if (snapshot is not null)
        {
            return new AudiencePrior(snapshot.PriorMean, snapshot.TmdbOffset);
        }

        // No import yet: shrink toward the catalog TMDB mean, with no offset.
        var tmdbMean = await db.Titles.AsNoTracking()
            .Where(row => row.VoteTmdb != null && row.VoteTmdbCount > 0)
            .AverageAsync(row => row.VoteTmdb, cancellationToken);
        return new AudiencePrior(tmdbMean ?? title.VoteTmdb ?? 0, 0);
    }

    private async Task<List<AudienceSample>> LoadCatalogSamplesAsync(
        ParsedImdbRatings parsed,
        CancellationToken cancellationToken)
    {
        var titles = await db.Titles.AsNoTracking()
            .Where(title => title.ImdbId != null)
            .Select(title => new { title.ImdbId, title.VoteTmdb, title.VoteTmdbCount })
            .ToListAsync(cancellationToken);
        var samples = new List<AudienceSample>(titles.Count);
        foreach (var title in titles)
        {
            if (parsed.Rows.TryGetValue(title.ImdbId!, out var row))
            {
                samples.Add(new AudienceSample(row.AverageRating, row.NumVotes, title.VoteTmdb, title.VoteTmdbCount));
            }
        }

        return samples;
    }

    private async Task ReplaceDumpAsync(
        ParsedImdbRatings parsed,
        AudiencePrior prior,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.ImdbTitleRatings.ExecuteDeleteAsync(cancellationToken);

        var buffer = new List<ImdbTitleRating>(InsertBatchSize);
        foreach (var row in parsed.Rows.Values)
        {
            buffer.Add(new ImdbTitleRating
            {
                Tconst = row.Tconst,
                AverageRating = row.AverageRating,
                NumVotes = row.NumVotes,
            });
            if (buffer.Count < InsertBatchSize)
            {
                continue;
            }

            db.ImdbTitleRatings.AddRange(buffer);
            await db.SaveChangesAsync(cancellationToken);
            buffer.Clear();
            db.ChangeTracker.Clear();
        }

        if (buffer.Count > 0)
        {
            db.ImdbTitleRatings.AddRange(buffer);
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }

        var snapshot = await db.ImdbRatingSnapshots
            .SingleOrDefaultAsync(row => row.Id == ImdbRatingSnapshot.SingletonId, cancellationToken);
        if (snapshot is null)
        {
            snapshot = new ImdbRatingSnapshot
            {
                Id = ImdbRatingSnapshot.SingletonId,
                SourceUrl = HttpImdbRatingsSource.DatasetUrl,
            };
            db.ImdbRatingSnapshots.Add(snapshot);
        }

        snapshot.MeanVote = parsed.MeanVote;
        snapshot.PriorMean = prior.Mean;
        snapshot.TmdbOffset = prior.TmdbOffset;
        snapshot.RowCount = parsed.Rows.Count;
        snapshot.ImportedAt = clock.UtcNow;
        snapshot.SourceUrl = HttpImdbRatingsSource.DatasetUrl;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    private async Task<int> ApplyToTitlesAsync(
        ParsedImdbRatings parsed,
        AudiencePrior prior,
        CancellationToken cancellationToken)
    {
        var updated = 0;
        var lastId = 0;
        while (true)
        {
            var batch = await db.Titles
                .Where(title => title.Id > lastId
                    && (title.ImdbId != null || title.VoteTmdb != null || title.VoteImdb != null))
                .OrderBy(title => title.Id)
                .Take(TitleBatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return updated;
            }

            foreach (var title in batch)
            {
                ApplyTitle(title, parsed, prior);
            }

            await db.SaveChangesAsync(cancellationToken);
            updated += batch.Count;
            lastId = batch[^1].Id;
            db.ChangeTracker.Clear();
        }
    }

    private static void ApplyTitle(Title title, ParsedImdbRatings parsed, AudiencePrior prior)
    {
        ImdbRatingRow? row = null;
        if (!string.IsNullOrWhiteSpace(title.ImdbId)
            && parsed.Rows.TryGetValue(title.ImdbId, out var found))
        {
            row = found;
        }

        ApplyStoredRating(title, row, prior);
    }

    private static void ApplyStoredRating(Title title, ImdbRatingRow? row, AudiencePrior prior)
    {
        title.VoteImdb = row?.AverageRating;
        title.VoteImdbCount = row?.NumVotes;
        title.VoteBlended = AudienceScore.Blend(
            row?.AverageRating,
            row?.NumVotes,
            title.VoteTmdb,
            title.VoteTmdbCount,
            prior);
    }
}

internal readonly record struct ImdbRatingRow(string Tconst, double AverageRating, int NumVotes);

internal sealed class ParsedImdbRatings(
    IReadOnlyDictionary<string, ImdbRatingRow> rows,
    double meanVote)
{
    public IReadOnlyDictionary<string, ImdbRatingRow> Rows { get; } = rows;
    public double MeanVote { get; } = meanVote;
}

internal static class ImdbRatingsFile
{
    public const string ExpectedHeader = "tconst\taverageRating\tnumVotes";

    public static ParsedImdbRatings Parse(Stream gzipStream)
    {
        Dictionary<string, ImdbRatingRow> rows;
        try
        {
            using var gzip = new GZipStream(gzipStream, CompressionMode.Decompress, leaveOpen: true);
            using var reader = new StreamReader(gzip, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var header = reader.ReadLine();
            if (!string.Equals(Normalize(header), ExpectedHeader, StringComparison.Ordinal))
            {
                throw new ImdbRatingImportException(
                    "IMDb ratings file header is not tconst, averageRating, numVotes.");
            }

            rows = new Dictionary<string, ImdbRatingRow>(StringComparer.Ordinal);
            while (reader.ReadLine() is { } line)
            {
                if (TryParseRow(line, out var row))
                {
                    rows[row.Tconst] = row;
                }
            }
        }
        catch (InvalidDataException ex)
        {
            throw new ImdbRatingImportException("IMDb ratings file is not a valid gzip archive.", ex);
        }

        if (rows.Count == 0)
        {
            throw new ImdbRatingImportException("IMDb ratings file has no usable rows.");
        }

        double weighted = 0;
        long votes = 0;
        foreach (var row in rows.Values)
        {
            weighted += row.AverageRating * row.NumVotes;
            votes += row.NumVotes;
        }

        if (votes <= 0)
        {
            throw new ImdbRatingImportException("IMDb ratings file has no usable rows.");
        }

        return new ParsedImdbRatings(rows, weighted / votes);
    }

    private static bool TryParseRow(string line, out ImdbRatingRow row)
    {
        row = default;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var parts = line.TrimEnd('\r').Split('\t');
        if (parts.Length != 3)
        {
            return false;
        }

        var tconst = parts[0].Trim();
        if (tconst.Length is < 3 or > 16
            || !tconst.StartsWith("tt", StringComparison.Ordinal)
            || !tconst[2..].All(char.IsAsciiDigit))
        {
            return false;
        }

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var rating)
            || !double.IsFinite(rating))
        {
            return false;
        }

        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var votes)
            || votes < 0)
        {
            return false;
        }

        row = new ImdbRatingRow(tconst, rating, votes);
        return true;
    }

    private static string Normalize(string? line) =>
        (line ?? "").Trim().TrimStart('\uFEFF').Replace("\r", "", StringComparison.Ordinal);
}
