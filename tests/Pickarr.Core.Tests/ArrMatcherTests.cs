using Pickarr.Core.Arr;
using Pickarr.Core.Domain;
using Pickarr.Core.Domain.Entities;
using Shouldly;

namespace Pickarr.Core.Tests;

public sealed class ArrMatcherTests
{
    [Fact]
    public void Matches_radarr_by_tmdb_movie()
    {
        var titles = new List<Title>
        {
            new()
            {
                Id = 1,
                MediaType = MediaTypes.Movie,
                Category = TitleCategories.Movie,
                TmdbId = 603,
                TmdbType = "movie",
                MetadataStatus = MetadataStatuses.Ready,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
        };
        var item = new ArrItemSnapshot(10, "Matrix", 1999, 603, null, null, true, null, 0, true, true, null, null, null, []);
        ArrMatcher.MatchTitleId(ArrKind.Radarr, item, titles).ShouldBe(1);
    }

    [Fact]
    public void Matches_sonarr_by_tvdb_then_tmdb()
    {
        var titles = new List<Title>
        {
            new()
            {
                Id = 2,
                MediaType = MediaTypes.Series,
                Category = TitleCategories.Series,
                TvdbId = 121361,
                TmdbId = 1396,
                TmdbType = "tv",
                MetadataStatus = MetadataStatuses.Ready,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
        };
        var item = new ArrItemSnapshot(55, "Series", 2018, 1396, 121361, null, true, null, 0, null, null, 1, 1, 1, []);
        ArrMatcher.MatchTitleId(ArrKind.Sonarr, item, titles).ShouldBe(2);
    }

    [Fact]
    public void Does_not_match_radarr_item_to_series_by_shared_imdb_id()
    {
        var series = new Title
        {
            Id = 3,
            MediaType = MediaTypes.Series,
            Category = TitleCategories.Series,
            ImdbId = "tt12590266",
            TmdbType = "tv",
            MetadataStatus = MetadataStatuses.Ready,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var item = new ArrItemSnapshot(
            512,
            "Cyberpunk: Edgerunners II",
            2026,
            1819914,
            null,
            "tt12590266",
            true,
            null,
            0,
            true,
            true,
            null,
            null,
            null,
            []);

        ArrMatcher.MatchTitleId(ArrKind.Radarr, item, [series]).ShouldBeNull();
    }

    [Fact]
    public void Does_not_match_sonarr_item_to_movie_by_shared_imdb_id()
    {
        var movie = new Title
        {
            Id = 4,
            MediaType = MediaTypes.Movie,
            Category = TitleCategories.Movie,
            ImdbId = "tt1234567",
            TmdbType = "movie",
            MetadataStatus = MetadataStatuses.Ready,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var item = new ArrItemSnapshot(
            77,
            "Series",
            2026,
            null,
            null,
            "tt1234567",
            true,
            null,
            0,
            null,
            null,
            null,
            null,
            null,
            []);

        ArrMatcher.MatchTitleId(ArrKind.Sonarr, item, [movie]).ShouldBeNull();
    }
}
