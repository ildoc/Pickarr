using Microsoft.Data.Sqlite;
using Pickarr.Core.Recommendations;

namespace Pickarr.RecoBench;

public sealed record BenchmarkTitleText(int TitleId, string Text);

public static class BenchmarkTextInput
{
    private sealed record Localization(string Language, string Name, string? Tagline, string? Overview);

    public static IReadOnlyList<BenchmarkTitleText> Read(SqliteConnection connection, BenchmarkCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(catalog);
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "PRAGMA query_only";
            if (Convert.ToInt64(check.ExecuteScalar()) != 1)
                throw new InvalidOperationException("A query-only snapshot connection is required.");
        }

        var poolIds = RecoMembership.PoolTitleIds(catalog.Availabilities, catalog.Sources).ToHashSet();
        using var transaction = connection.BeginTransaction(deferred: true);

        var localizations = new Dictionary<int, List<Localization>>();
        using (var command = Query("""
            SELECT TitleId, Language, Name, Tagline, Overview
            FROM TitleLocalizations
            ORDER BY TitleId, Language
            """))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var titleId = reader.GetInt32(0);
                if (!localizations.TryGetValue(titleId, out var values))
                    localizations[titleId] = values = [];
                values.Add(new Localization(reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }

        var genres = new Dictionary<int, List<(int GenreId, string Language, string Name)>>();
        using (var command = Query("""
            SELECT tg.TitleId, tg.GenreId, gl.Language, gl.Name
            FROM TitleGenres tg
            JOIN GenreLocalizations gl ON gl.GenreId = tg.GenreId
            ORDER BY tg.TitleId, tg.GenreId, gl.Language
            """))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var titleId = reader.GetInt32(0);
                if (!genres.TryGetValue(titleId, out var values))
                    genres[titleId] = values = [];
                values.Add((reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
            }

        var keywords = new Dictionary<int, List<string>>();
        using (var command = Query("""
            SELECT tk.TitleId, k.Name
            FROM TitleKeywords tk
            JOIN Keywords k ON k.Id = tk.KeywordId
            ORDER BY tk.TitleId, tk.KeywordId
            """))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var titleId = reader.GetInt32(0);
                if (!keywords.TryGetValue(titleId, out var values))
                    keywords[titleId] = values = [];
                values.Add(reader.GetString(1));
            }

        var texts = new List<BenchmarkTitleText>(poolIds.Count);
        using (var command = Query("SELECT Id, OriginalTitle, ReleaseYear FROM Titles ORDER BY Id"))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                if (!poolIds.Contains(id))
                    continue;

                var titleLocalizations = localizations.GetValueOrDefault(id) ?? [];
                var localization = titleLocalizations.FirstOrDefault(x =>
                    string.Equals(x.Language, "en", StringComparison.OrdinalIgnoreCase))
                    ?? titleLocalizations.FirstOrDefault();
                var name = localization?.Name
                    ?? (reader.IsDBNull(1) ? $"Title {id}" : reader.GetString(1));
                var titleGenres = (genres.GetValueOrDefault(id) ?? [])
                    .GroupBy(x => x.GenreId)
                    .OrderBy(x => x.Key)
                    .Select(group => group.FirstOrDefault(x =>
                        string.Equals(x.Language, "en", StringComparison.OrdinalIgnoreCase)).Name
                        ?? group.First().Name)
                    .ToArray();
                var titleKeywords = keywords.GetValueOrDefault(id) ?? [];
                var text = RecoEmbeddingText.Build(
                    name,
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    titleGenres,
                    titleKeywords,
                    localization?.Tagline,
                    localization?.Overview);
                texts.Add(new BenchmarkTitleText(id, text));
            }

        if (texts.Count != poolIds.Count)
            throw new InvalidOperationException("Text metadata is missing for a catalog-pool title.");
        transaction.Commit();
        return texts;

        SqliteCommand Query(string sql)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            return command;
        }
    }

    public static IReadOnlyDictionary<int, string> ReadCurrentEmbeddingTextHashes(
        SqliteConnection connection,
        string modelId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "PRAGMA query_only";
            if (Convert.ToInt64(check.ExecuteScalar()) != 1)
                throw new InvalidOperationException("A query-only snapshot connection is required.");
        }

        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT TitleId, TextHash FROM TitleEmbeddings WHERE ModelId = $model ORDER BY TitleId";
        command.Parameters.AddWithValue("$model", modelId);
        var hashes = new Dictionary<int, string>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                hashes.Add(reader.GetInt32(0), reader.GetString(1));
        transaction.Commit();
        return hashes;
    }
}
