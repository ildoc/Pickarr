namespace Pickarr.Core.Metadata;

/// <summary>
/// Stand-up detection from TMDB keyword <em>names</em> (D-06 / BR-02). A match adds the synthetic
/// Stand-up genre; it does not change the title type.
/// Does not hardcode TMDB keyword ids — those can collide or drift.
/// </summary>
public static class TmdbStandUp
{
    public static bool IsStandUpKeyword(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // "stand-up comedy", "standup comedy", "stand–up comedy", …
        var normalized = name.Trim().ToLowerInvariant()
            .Replace('–', '-')
            .Replace('—', '-')
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("_", "");
        return normalized == "standupcomedy";
    }

    public static bool HasStandUpKeyword(IEnumerable<string?> names) =>
        names.Any(IsStandUpKeyword);
}
