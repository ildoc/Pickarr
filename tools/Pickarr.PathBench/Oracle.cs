using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pickarr.Core.Domain;
using Pickarr.Core.Recommendations;
using Pickarr.Infrastructure.Persistence;

namespace Pickarr.PathBench;

// Independent full-corpus reference: no production pool/ranking, vector,
// normalization, graph, filter or top-k helpers. Never serialized.
internal sealed class Oracle
{
    public Dictionary<int, Dictionary<string, double>> Terms { get; } = [];
    public Dictionary<int, float[]> Dense { get; } = [];
    public Dictionary<(int Source, int Target), int> Edges { get; } = [];
    public HashSet<int> Excluded { get; } = [];

    public static async Task<Oracle> Load(PickarrDbContext db, int profile)
    {
        var o = new Oracle();
        var pool = db.Availabilities.Where(a => a.CatalogSource!.IsEnabled && a.CatalogSource.Provider!.IsEnabled)
            .Select(a => a.TitleId).Distinct();
        foreach (var t in await db.Titles.Where(t => pool.Contains(t.Id))
            .Select(t => new { t.Id, t.Category, t.OriginalLanguage, t.OriginCountries, t.ReleaseYear }).ToListAsync())
        {
            var terms = o.Terms[t.Id] = [];
            if (!string.IsNullOrWhiteSpace(t.Category)) terms["type:" + t.Category] = .5;
            if (!string.IsNullOrWhiteSpace(t.OriginalLanguage)) terms["lang:" + t.OriginalLanguage] = .3;
            if (t.ReleaseYear is > 0) terms["decade:" + (t.ReleaseYear / 10)] = .3;
            if (t.OriginCountries is not null)
            {
                string[] countries;
                try { countries = JsonSerializer.Deserialize<string[]>(t.OriginCountries) ?? []; }
                catch (JsonException) { countries = []; }
                foreach (var c in countries.Where(c => !string.IsNullOrWhiteSpace(c))) terms["country:" + c.Trim()] = .3;
            }
        }
        foreach (var g in await db.TitleGenres.Where(g => pool.Contains(g.TitleId)).Select(g => new { g.TitleId, g.GenreId }).ToListAsync())
            o.Terms[g.TitleId]["genre:" + g.GenreId] = 1;
        foreach (var k in await db.TitleKeywords.Where(k => pool.Contains(k.TitleId)).Select(k => new { k.TitleId, k.KeywordId }).ToListAsync())
            o.Terms[k.TitleId]["keyword:" + k.KeywordId] = 1;
        foreach (var c in await db.TitleCredits.Where(c => pool.Contains(c.TitleId)).Select(c => new { c.TitleId, c.PersonId, c.Role }).ToListAsync())
        {
            if (c.Role == CreditRoles.Cast) o.Terms[c.TitleId]["cast:" + c.PersonId] = .5;
            if (c.Role is CreditRoles.Director or CreditRoles.Creator) o.Terms[c.TitleId]["director:" + c.PersonId] = .8;
        }
        var df = o.Terms.Values.SelectMany(t => t.Keys).CountBy(k => k).ToDictionary();
        foreach (var terms in o.Terms.Values)
        {
            foreach (var k in terms.Keys.ToArray())
                terms[k] *= 1 + Math.Log((1d + o.Terms.Count) / (1 + df[k]));
            var norm = Math.Sqrt(terms.Values.Sum(x => x * x));
            foreach (var k in terms.Keys.ToArray()) terms[k] /= norm;
        }
        foreach (var e in await db.TitleEmbeddings.Where(e => pool.Contains(e.TitleId) && e.ModelId == RecoModelCatalog.DefaultModelId)
            .Select(e => new { e.TitleId, e.Vector }).ToListAsync())
        {
            var v = new float[RecoModelCatalog.EmbeddingDimensions];
            if (e.Vector.Length != v.Length * sizeof(float)) throw new InvalidDataException("Embedding dimensions.");
            Buffer.BlockCopy(e.Vector, 0, v, 0, e.Vector.Length);
            if (Math.Abs(v.Sum(x => (double)x * x) - 1) > .01 || v.Any(x => !float.IsFinite(x)))
                throw new InvalidDataException("Expected normalized finite vectors.");
            o.Dense.Add(e.TitleId, v);
        }
        foreach (var e in await db.TitleTmdbRecommendations.Where(e => pool.Contains(e.SourceTitleId) && pool.Contains(e.RelatedTitleId))
            .Select(e => new { e.SourceTitleId, e.RelatedTitleId, e.Rank }).ToListAsync())
            o.Edges[(e.SourceTitleId, e.RelatedTitleId)] = e.Rank;
        o.Excluded.UnionWith(await db.Ratings.Where(r => r.ProfileId == profile).Select(r => r.TitleId).ToListAsync());
        o.Excluded.UnionWith(await db.Watched.Where(r => r.ProfileId == profile).Select(r => r.TitleId).ToListAsync());
        if (await db.Profiles.Where(p => p.Id == profile).Select(p => p.ExcludeInLibrary).SingleAsync())
            o.Excluded.UnionWith(await db.ArrLibraryItems.Where(i => i.TitleId != null).Select(i => i.TitleId!.Value).ToListAsync());
        return o;
    }

    public int[] Rank(int query, int k)
    {
        var scored = new List<(int Id, double Score)>();
        foreach (var (id, terms) in Terms)
        {
            if (id == query || Excluded.Contains(id)) continue;
            bool dense = Dense.ContainsKey(query) && Dense.ContainsKey(id);
            var sim = dense ? Dense[query].Select((x, i) => (double)x * Dense[id][i]).Sum()
                : Terms[query].Sum(t => t.Value * terms.GetValueOrDefault(t.Key));
            var score = sim < .01 ? 0 : dense ? Math.Clamp((sim + 1) / 2, 0, 1) : sim;
            if (Edges.TryGetValue((query, id), out var rank) && rank >= 1)
                score += .15 * Math.Max(0, 21 - rank) / 20;
            if (score > 0) scored.Add((id, score));
        }
        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.Id).Take(k).Select(s => s.Id).ToArray();
    }
}
