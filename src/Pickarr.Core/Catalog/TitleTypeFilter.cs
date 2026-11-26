using Pickarr.Core.Domain;

namespace Pickarr.Core.Catalog;

/// <summary>
/// Maps the <c>type</c> query values to stored categories. The legacy <c>standup</c> type is now the
/// synthetic Stand-up genre, so old links and remembered filters keep working.
/// </summary>
public static class TitleTypeFilter
{
    public static IReadOnlyList<string> Categories(IReadOnlyList<string>? types) =>
        types is null
            ? []
            : types
                .Select(MapCategory)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();

    public static IReadOnlyList<int>? GenresWithLegacyTypes(
        IReadOnlyList<string>? types,
        IReadOnlyList<int>? genres)
    {
        if (types is null || !types.Any(IsLegacyStandUp))
        {
            return genres;
        }

        return (genres ?? []).Append(SyntheticGenres.StandUp).Distinct().ToList();
    }

    public static string ToApiType(string category) => category switch
    {
        TitleCategories.Movie => "movie",
        TitleCategories.Series => "series",
        _ => category.ToLowerInvariant(),
    };

    private static string? MapCategory(string type) => type.Trim().ToLowerInvariant() switch
    {
        "movie" => TitleCategories.Movie,
        "series" => TitleCategories.Series,
        _ => null,
    };

    private static bool IsLegacyStandUp(string type) =>
        type.Trim().ToLowerInvariant() is "standup" or "stand-up" or "stand_up";
}
