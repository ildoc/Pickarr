using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pickarr.Core.Domain;
using Pickarr.Core.Recommendations;

namespace Pickarr.RecoBench;

public static class BenchmarkCatalogInput
{
    public static BenchmarkCatalog Read(SqliteConnection connection, string modelId, int dimensions)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (string.IsNullOrWhiteSpace(modelId) || dimensions <= 0)
            throw new ArgumentException("Model identity and dimension are required.");
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "PRAGMA query_only";
            if (Convert.ToInt64(check.ExecuteScalar()) != 1)
                throw new InvalidOperationException("A query-only snapshot connection is required.");
        }

        using var transaction = connection.BeginTransaction(deferred: true);
        SqliteCommand Command(string sql)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            return command;
        }

        var genres = new Dictionary<int, List<int>>();
        var keywords = new Dictionary<int, List<int>>();
        var cast = new Dictionary<int, List<int>>();
        var directors = new Dictionary<int, List<int>>();
        static void Add(Dictionary<int, List<int>> map, int title, int value)
        {
            if (!map.TryGetValue(title, out var values))
                map[title] = values = [];
            values.Add(value);
        }

        using (var command = Command("SELECT TitleId, GenreId FROM TitleGenres"))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) Add(genres, reader.GetInt32(0), reader.GetInt32(1));
        using (var command = Command("SELECT TitleId, KeywordId FROM TitleKeywords"))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) Add(keywords, reader.GetInt32(0), reader.GetInt32(1));
        using (var command = Command("SELECT TitleId, PersonId, Role FROM TitleCredits"))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var role = reader.GetString(2);
                if (role == CreditRoles.Cast) Add(cast, reader.GetInt32(0), reader.GetInt32(1));
                if (role is CreditRoles.Director or CreditRoles.Creator)
                    Add(directors, reader.GetInt32(0), reader.GetInt32(1));
            }

        var embeddings = new Dictionary<int, float[]>();
        using (var command = Command("SELECT TitleId, Vector FROM TitleEmbeddings WHERE ModelId = $model"))
        {
            command.Parameters.AddWithValue("$model", modelId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var bytes = (byte[])reader.GetValue(1);
                if (bytes.Length != (long)dimensions * sizeof(float))
                    throw new InvalidOperationException("Invalid embedding dimensions.");
                var vector = new float[dimensions];
                Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
                embeddings.Add(reader.GetInt32(0), vector);
            }
        }

        var titles = new List<BenchmarkTitle>();
        using (var command = Command("""
            SELECT Id, Category, OriginalLanguage, ReleaseYear, OriginCountries, VoteTmdb, VoteTmdbCount, Popularity
            FROM Titles
            """))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                var year = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
                var countries = reader.IsDBNull(4) ? [] :
                    JsonSerializer.Deserialize<List<string>>(reader.GetString(4)) ?? [];
                var features = new RecoSparseTitleFeatures(id, reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2), year,
                    genres.GetValueOrDefault(id) ?? [], keywords.GetValueOrDefault(id) ?? [],
                    cast.GetValueOrDefault(id) ?? [], directors.GetValueOrDefault(id) ?? [], countries);
                var stats = new RecoTitleStats(id, reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetDouble(7));
                var vector = embeddings.GetValueOrDefault(id);
                titles.Add(new BenchmarkTitle(features, stats, vector is null ? null : modelId, vector));
            }

        var sources = new List<RecoSourceFact>();
        using (var command = Command("""
            SELECT s.Id, s.IsEnabled, COALESCE(p.IsEnabled, 0)
            FROM CatalogSources s LEFT JOIN Providers p ON p.Id = s.ProviderId
            """))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) sources.Add(new(reader.GetInt32(0), reader.GetBoolean(1), reader.GetBoolean(2)));
        var availabilities = new List<RecoAvailabilityFact>();
        using (var command = Command("SELECT TitleId, CatalogSourceId FROM Availabilities"))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) availabilities.Add(new(reader.GetInt32(0), reader.GetInt32(1)));
        transaction.Commit();
        return new BenchmarkCatalog(titles, availabilities, sources, modelId, dimensions);
    }
}
