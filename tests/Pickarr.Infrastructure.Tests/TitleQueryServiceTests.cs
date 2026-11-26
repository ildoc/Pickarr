using System.Text.Json;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Catalog;
using Pickarr.Core.Domain;
using Pickarr.Core.Domain.Entities;
using Pickarr.Core.Metadata;
using Pickarr.Core.Recommendations;
using Pickarr.Core.Settings;
using Pickarr.Infrastructure.Catalog;
using Pickarr.Infrastructure.Persistence;
using Shouldly;
using Xunit.Abstractions;

namespace Pickarr.Infrastructure.Tests;

public sealed class TitleQueryServiceTests(ITestOutputHelper output)
{
    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class MemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, (string Json, bool Secret)> _values = new(StringComparer.Ordinal);

        public Task<string?> GetRawAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.TryGetValue(key, out var v) ? v.Json : null);

        public Task SetAsync(string key, string valueJson, bool isSecret, CancellationToken cancellationToken = default)
        {
            _values[key] = (valueJson, isSecret);
            return Task.CompletedTask;
        }

        public Task<bool> IsSetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.ContainsKey(key));

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Detail_links_to_cached_arr_slug_and_preserves_url_base()
    {
        await using var db = await CreateDbAsync();
        var source = await SeedCatalogAsync(db);
        var titles = await SeedTitlesAsync(db, source.Id);
        var settings = await ConfigureArrAsync(sonarr: true, radarr: true);
        await settings.SetAsync(SettingsKeys.SonarrUrl, JsonSerializer.Serialize("http://sonarr/base/"), false);
        await settings.SetAsync(SettingsKeys.RadarrUrl, JsonSerializer.Serialize("http://radarr/base/"), false);
        db.ArrLibraryItems.AddRange(
            new ArrLibraryItem { TitleId = titles.Series.Id, Kind = ArrKinds.Sonarr, ArrId = 55, ArrTitle = "Series", TitleSlug = "series-slug", State = ArrStates.Downloaded },
            new ArrLibraryItem { TitleId = titles.Movie.Id, Kind = ArrKinds.Radarr, ArrId = 10, ArrTitle = "Movie", TitleSlug = "movie-slug", State = ArrStates.Downloaded });
        await db.SaveChangesAsync();
        var sut = new TitleQueryService(db, settings, new FixedClock(DateTime.UtcNow));

        (await sut.GetTitleAsync(titles.Series.Id, source.Id))!.Arr.ExternalUrl.ShouldBe("http://sonarr/base/series/series-slug");
        (await sut.GetTitleAsync(titles.Movie.Id, source.Id))!.Arr.ExternalUrl.ShouldBe("http://radarr/base/movie/movie-slug");
        var item = await db.ArrLibraryItems.SingleAsync(x => x.TitleId == titles.Movie.Id);
        item.TitleSlug = null;
        await db.SaveChangesAsync();
        (await sut.GetTitleAsync(titles.Movie.Id, source.Id))!.Arr.ExternalUrl.ShouldBeNull();
    }

    [Fact]
    public async Task Query_without_Arr_still_returns_titles_for_browse()
    {
        await using var db = await CreateDbAsync();
        var source = await SeedCatalogAsync(db);
        await SeedTitlesAsync(db, source.Id);
        var settings = new MemorySettingsStore();
        var sut = new TitleQueryService(db, settings, new FixedClock(DateTime.UtcNow));

        var result = await sut.QueryTitlesAsync(source.Id, new TitleQueryRequest());

        result.EmptyReason.ShouldBeNull();
        result.Total.ShouldBe(3);
        result.ArrFilter!.SonarrConfigured.ShouldBeFalse();
        result.ArrFilter.RadarrConfigured.ShouldBeFalse();
        result.ArrFilter.AllowedMediaTypes.ShouldBe([MediaTypes.Series, MediaTypes.Movie]);
    }

    [Fact]
    public async Task MinRating_and_rating_sort_use_the_blended_score()
    {
        await using var db = await CreateDbAsync();
        var source = await SeedCatalogAsync(db);
        var titles = await SeedTitlesAsync(db, source.Id);
        titles.Movie.VoteTmdb = 9;
        titles.Movie.VoteBlended = 6;
        titles.Series.VoteTmdb = 5;
        titles.Series.VoteBlended = 8;
        titles.StandUp.VoteTmdb = 7.5;
        titles.StandUp.VoteBlended = null;
        await db.SaveChangesAsync();
        var sut = new TitleQueryService(db, new MemorySettingsStore(), new FixedClock(DateTime.UtcNow));

        var filtered = await sut.QueryTitlesAsync(source.Id, new TitleQueryRequest(MinRating: 7));
        filtered.Items.Select(item => item.Id).ShouldBe([titles.Series.Id]);

        var descending = await sut.QueryTitlesAsync(
            source.Id,
            new TitleQueryRequest(Sort: "rating", Dir: "desc"));
        descending.Items.Select(item => item.Id).ShouldBe([titles.Series.Id, titles.Movie.Id, titles.StandUp.Id]);

        var ascending = await sut.QueryTitlesAsync(
            source.Id,
            new TitleQueryRequest(Sort: "rating", Dir: "asc"));
        ascending.Items.Select(item => item.Id).ShouldBe([titles.StandUp.Id, titles.Movie.Id, titles.Series.Id]);
    }

    [Fact]
    public async Task List_and_detail_expose_the_blended_score_and_both_sources()
    {
        await using var db = await CreateDbAsync();
        var source = await SeedCatalogAsync(db);
        var titles = await SeedTitlesAsync(db, source.Id);
        titles.Movie.VoteTmdb = 6.2;
        titles.Movie.VoteTmdbCount = 80;
        titles.Movie.VoteImdb = 8;
        titles.Movie.VoteImdbCount = 500;
        titles.Movie.VoteBlended = 7.4;
        await db.SaveChangesAsync();
        var sut = new TitleQueryService(db, new MemorySettingsStore(), new FixedClock(DateTime.UtcNow));

        var item = (await sut.QueryTitlesAsync(source.Id, new TitleQueryRequest()))
            .Items.Single(row => row.Id == titles.Movie.Id);
        item.RatingTmdb.ShouldBe(6.2);
        item.RatingScore.ShouldBe(7.4);

        var detail = (await sut.GetTitleAsync(titles.Movie.Id, source.Id))!;
        detail.Ratings.Score.ShouldBe(7.4);
        detail.Ratings.Imdb.ShouldBe(8);
        detail.Ratings.ImdbVotes.ShouldBe(500);
        detail.Ratings.Tmdb.ShouldBe(6.2);
        detail.Ratings.TmdbVotes.ShouldBe(80);
    }

    [Fact]
    public async Task Single_source_title_shows_its_raw_rating_but_ranks_by_the_blend()
    {
        await using var db = await CreateDbAsync();
        var source = await SeedCatalogAsync(db);
        var titles = await SeedTitlesAsync(db, source.Id);
        titles.Movie.VoteTmdb = 9.5;
        titles.Movie.VoteTmdbCount = 3;
        titles.Movie.VoteBlended = 7.02;
        titles.Series.VoteTmdb = 7.6;
        titles.Series.VoteTmdbCount = 900;
        titles.Series.VoteImdb = 7.8;
        titles.Series.VoteImdbCount = 40_000;
        titles.Series.VoteBlended = 7.79;
        titles.StandUp.VoteTmdb = 0;
        titles.StandUp.VoteTmdbCount = 0;
        titles.StandUp.VoteBlended = null;
        await db.SaveChangesAsync();
        var sut = new TitleQueryService(db, new MemorySettingsStore(), new FixedClock(DateTime.UtcNow));

        var page = await sut.QueryTitlesAsync(source.Id, new TitleQueryRequest(Sort: "rating", Dir: "desc"));
        page.Items.Select(item => item.Id).ShouldBe([titles.Series.Id, titles.Movie.Id, titles.StandUp.Id]);
        page.Items.Single(item => item.Id == titles.Movie.Id).RatingScore.ShouldBe(9.5);
        page.Items.Single(item => item.Id == titles.Series.Id).RatingScore.ShouldBe(7.79);
        page.Items.Single(item => item.Id == titles.StandUp.Id).RatingScore.ShouldBeNull();

        var filtered = await sut.QueryTitlesAsync(source.Id, new TitleQueryRequest(MinRating: 7.5));
        filtered.Items.Select(item => item.Id).ShouldBe([titles.Series.Id]);

        (await sut.GetTitleAsync(titles.Movie.Id, source.Id))!.Ratings.Score.ShouldBe(9.5);
    }

    [Fact]
    public async Task Query_without_an_import_explains_the_empty_catalog()
    {
        await using var db = await CreateDbAsync();
        var source = await SeedCatalogAsync(db);
        source.BaselineCompletedAt = null;
        source.LastFullAt = null;
        source.LastIncrementalAt = null;
        await db.SaveChangesAsync();
        var settings = await ConfigureArrAsync(sonarr: true, radarr: true);
        var sut = new TitleQueryService(db, settings, new FixedClock(DateTime.UtcNow));

        var waiting = await sut.QueryTitlesAsync(source.Id, new TitleQueryRequest());
        waiting.Total.ShouldBe(0);
        waiting.EmptyReason.ShouldBe(CatalogEmptyReasons.NotImported);

        db.SyncRuns.Add(new SyncRun
        {
            Id = Guid.NewGuid(),
            Kind = SyncRunKinds.CatalogIncremental,
            CatalogSourceId = source.Id,
            TriggeredBy = SyncRunTriggers.Schedule,
            StartedAt = DateTime.UtcNow,
            Status = SyncRunStatuses.Running,
        });
        await db.SaveChangesAsync();

        var importing = await sut.QueryTitlesAsync(source.Id, new TitleQueryRequest());
        importing.EmptyReason.ShouldBe(CatalogEmptyReasons.Importing);
    }

    [Fact]
    public async Task Query_is_scoped_to_the_selected_country()
    {
        await using var db = await CreateDbAsync();
        var italy = await SeedCatalogAsync(db);
        var unitedStates = new CatalogSource
        {
            ProviderId = italy.ProviderId,
            CountryCode = "US",
            MetadataLanguage = "en-US",
            IsEnabled = true,
            BaselineCompletedAt = DateTime.UtcNow,
            LastFullAt = DateTime.UtcNow,
        };
        db.CatalogSources.Add(unitedStates);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var onlyUs = new Title
        {
            MediaType = MediaTypes.Movie,
            Category = TitleCategories.Movie,
            OriginalTitle = "Only In America",
            OriginalTitleNormalized = "only in america",
            ReleaseYear = 2024,
            MetadataStatus = MetadataStatuses.Ready,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Titles.Add(onlyUs);
        await db.SaveChangesAsync();
        db.Availabilities.Add(new Availability
        {
            TitleId = onlyUs.Id,
            CatalogSourceId = unitedStates.Id,
            FirstSeenAt = now,
            LastSeenAt = now,
            AddedAt = now,
        });
        await db.SaveChangesAsync();

        var settings = await ConfigureArrAsync(sonarr: true, radarr: true);
        var sut = new TitleQueryService(db, settings, new FixedClock(now));

        var us = await sut.QueryTitlesAsync(unitedStates.Id, new TitleQueryRequest());
        var it = await sut.QueryTitlesAsync(italy.Id, new TitleQueryRequest());

        us.Items.ShouldContain(item => item.OriginalTitle == "Only In America");
        it.Items.ShouldNotContain(item => item.OriginalTitle == "Only In America");
    }

    [Fact]
    public async Task Query_with_Sonarr_only_still_lists_movies_and_blocks_movie_add()
    {
        await using var db = await CreateDbAsync();
        var source = await SeedCatalogAsync(db);
        var titles = await SeedTitlesAsync(db, source.Id);
        var settings = await ConfigureArrAsync(sonarr: true, radarr: false);
        var sut = new TitleQueryService(db, settings, new FixedClock(new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc)));

        var result = await sut.QueryTitlesAsync(source.Id, new TitleQueryRequest());

        result.EmptyReason.ShouldBeNull();
        result.Total.ShouldBe(3);
        result.Items.Select(i => i.Category).OrderBy(x => x).ShouldBe(["movie", "series", "standup"]);
        result.ArrFilter!.SonarrConfigured.ShouldBeTrue();
        result.ArrFilter.RadarrConfigured.ShouldBeFalse();

        var movieDetail = await sut.GetTitleAsync(titles.Movie.Id, source.Id);
        movieDetail!.Arr.CanAdd.ShouldBeFalse();
        movieDetail.Arr.AddBlockedReason.ShouldBe("notConfigured");

        var seriesDetail = await sut.GetTitleAsync(titles.Series.Id, source.Id);
        seriesDetail!.Arr.CanAdd.ShouldBeTrue();
        seriesDetail.Arr.AddBlockedReason.ShouldBeNull();
    }

    [Fact]
    public async Task Detail_returns_structured_cast_and_preserves_historical_fallback()
    {
        await using var db = await CreateDbAsync();
        var source = await SeedCatalogAsync(db);
        var titles = await SeedTitlesAsync(db, source.Id);
        var person = new CastPerson
        {
            Id = 42,
            Name = "Second Actor",
            ProfilePath = "/second.jpg",
            ImdbId = "nm0000042",
        };
        db.CastPeople.Add(person);
        titles.Movie.CastFetchedAt = DateTime.UtcNow;
        titles.Movie.CastMembers.Add(new TitleCastMember
        {
            Person = person,
            Position = 1,
        });
        titles.Series.CastJson = JsonSerializer.Serialize(new[] { "Historical Actor" });
        await db.SaveChangesAsync();

        var sut = new TitleQueryService(db, new MemorySettingsStore(), new FixedClock(DateTime.UtcNow));

        var detail = (await sut.GetTitleAsync(titles.Movie.Id, source.Id))!;
        detail.CastMembers.ShouldHaveSingleItem();
        detail.CastMembers[0].Name.ShouldBe("Second Actor");
        detail.CastMembers[0].PhotoUrl.ShouldBe("https://image.tmdb.org/t/p/w185/second.jpg");
        detail.CastMembers[0].ImdbUrl.ShouldBe("https://www.imdb.com/name/nm0000042/");
        detail.Cast.ShouldBeEmpty();

        var fallback = (await sut.GetTitleAsync(titles.Series.Id, source.Id))!;
        fallback.CastMembers.ShouldHaveSingleItem();
        fallback.CastMembers[0].Name.ShouldBe("Historical Actor");
        fallback.CastMembers[0].PhotoUrl.ShouldBeNull();
        fallback.CastMembers[0].ImdbUrl.ShouldBeNull();
    }

    [Fact]
    public async Task Detail_does_not_resurrect_historical_cast_after_empty_fetch()
    {
        await using var db = await CreateDbAsync();
        var source = await SeedCatalogAsync(db);
        var titles = await SeedTitlesAsync(db, source.Id);
        titles.Movie.CastJson = JsonSerializer.Serialize(new[] { "Obsolete Actor" });
        titles.Movie.CastFetchedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var sut = new TitleQueryService(db, new MemorySettingsStore(), new FixedClock(DateTime.UtcNow));

        var detail = (await sut.GetTitleAsync(titles.Movie.Id, source.Id))!;
        detail.CastMembers.ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_is_accent_insensitive_on_localizations_and_original()
    {
        await using var db = await CreateDbAsync();
        var source = await SeedCatalogAsync(db);
        await SeedTitlesAsync(db, source.Id);
        var settings = await ConfigureArrAsync(sonarr: true, radarr: true);
        var sut = new TitleQueryService(db, settings, new FixedClock(DateTime.UtcNow));

        var byLoc = await sut.QueryTitlesAsync(source.Id, new TitleQueryRequest(Q: "caffe"));
        byLoc.Items.ShouldContain(i => i.Name.Contains("Caffè", StringComparison.Ordinal));

        var byOriginal = await sut.QueryTitlesAsync(source.Id, new TitleQueryRequest(Q: "matrix"));
        byOriginal.Items.ShouldContain(i => i.OriginalTitle == "The Matrix");
    }

    [Fact]
    public async Task Favorites_are_global_and_include_missing_availability()
    {
        await using var db = await CreateDbAsync();
        var source = await SeedCatalogAsync(db);
        var titles = await SeedTitlesAsync(db, source.Id);
        var orphan = new Title
        {
            MediaType = MediaTypes.Movie,
            Category = TitleCategories.Movie,
            OriginalTitle = "Orphan Fav",
            OriginalTitleNormalized = "orphan fav",
            MetadataStatus = MetadataStatuses.Ready,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Titles.Add(orphan);
        await db.SaveChangesAsync();
        var profile = new Profile { Name = "List", Color = Profile.DefaultColor, CreatedAt = DateTime.UtcNow };
        db.Favorites.Add(new Favorite { Profile = profile, TitleId = orphan.Id, CreatedAt = DateTime.UtcNow });
        db.Favorites.Add(new Favorite { Profile = profile, TitleId = titles.Movie.Id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var settings = await ConfigureArrAsync(sonarr: false, radarr: true);
        var sut = new TitleQueryService(db, settings, new FixedClock(DateTime.UtcNow));

        var result = await sut.QueryTitlesAsync(
            source.Id,
            new TitleQueryRequest(Favorite: true, IncludeRemoved: true),
            personalProfileId: profile.Id);

        result.Total.ShouldBe(2);
        result.Items.ShouldContain(i => i.Id == orphan.Id && i.Availability == null);
        result.Items.ShouldContain(i => i.Id == titles.Movie.Id && i.IsFavorite);
    }

    [Fact]
    public async Task Catalog_flags_follow_the_declared_profile_and_stay_empty_without_one()
    {
        await using var db = await CreateDbAsync();
        var source = await SeedCatalogAsync(db);
        var titles = await SeedTitlesAsync(db, source.Id);
        var now = DateTime.UtcNow;
        var admin = new Profile { Name = "Admin", Color = Profile.DefaultColor, IsAdmin = true, CreatedAt = now };
        var other = new Profile { Name = "Other", Color = "teal", CreatedAt = now };
        db.Profiles.AddRange(admin, other);
        db.Favorites.Add(new Favorite { Profile = admin, TitleId = titles.Movie.Id, CreatedAt = now });
        db.Watched.Add(new Watched { Profile = admin, TitleId = titles.Movie.Id, WatchedAt = now });
        db.Ratings.Add(new Rating { Profile = admin, TitleId = titles.Movie.Id, Value = RatingValues.Love, RatedAt = now });
        db.Ratings.Add(new Rating { Profile = other, TitleId = titles.Movie.Id, Value = RatingValues.Dislike, RatedAt = now });
        await db.SaveChangesAsync();

        var settings = await ConfigureArrAsync(sonarr: true, radarr: true);
        var sut = new TitleQueryService(db, settings, new FixedClock(now));
        var request = new TitleQueryRequest();

        var forAdmin = await sut.QueryTitlesAsync(source.Id, request, personalProfileId: admin.Id);
        var adminItem = forAdmin.Items.Single(i => i.Id == titles.Movie.Id);
        adminItem.IsFavorite.ShouldBeTrue();
        adminItem.IsWatched.ShouldBeTrue();
        adminItem.Rating.ShouldBe("love");

        var forOther = await sut.QueryTitlesAsync(source.Id, request, personalProfileId: other.Id);
        var otherItem = forOther.Items.Single(i => i.Id == titles.Movie.Id);
        otherItem.IsFavorite.ShouldBeFalse();
        otherItem.IsWatched.ShouldBeFalse();
        otherItem.Rating.ShouldBe("dislike");

        var anonymous = await sut.QueryTitlesAsync(source.Id, request);
        anonymous.Total.ShouldBe(3);
        var bare = anonymous.Items.Single(i => i.Id == titles.Movie.Id);
        bare.IsFavorite.ShouldBeFalse();
        bare.IsWatched.ShouldBeFalse();
        bare.Rating.ShouldBeNull();

        var adminDetail = await sut.GetTitleAsync(titles.Movie.Id, source.Id, personalProfileId: admin.Id);
        adminDetail!.IsFavorite.ShouldBeTrue();
        adminDetail.IsWatched.ShouldBeTrue();
        adminDetail.Rating.ShouldBe("love");

        var otherDetail = await sut.GetTitleAsync(titles.Movie.Id, source.Id, personalProfileId: other.Id);
        otherDetail!.IsFavorite.ShouldBeFalse();
        otherDetail.IsWatched.ShouldBeFalse();
        otherDetail.Rating.ShouldBe("dislike");

        var bareDetail = await sut.GetTitleAsync(titles.Movie.Id, source.Id);
        bareDetail!.IsFavorite.ShouldBeFalse();
        bareDetail.IsWatched.ShouldBeFalse();
        bareDetail.Rating.ShouldBeNull();
    }

    [Fact]
    public async Task Favorites_include_titles_from_other_countries_and_languages()
    {
        await using var db = await CreateDbAsync();
        var italy = await SeedCatalogAsync(db);
        var unitedStates = new CatalogSource
        {
            ProviderId = italy.ProviderId,
            CountryCode = "US",
            MetadataLanguage = "en-US",
            IsEnabled = true,
            BaselineCompletedAt = DateTime.UtcNow,
            LastFullAt = DateTime.UtcNow,
        };
        db.CatalogSources.Add(unitedStates);
        var overseas = new Title
        {
            MediaType = MediaTypes.Movie,
            Category = TitleCategories.Movie,
            OriginalTitle = "Overseas Only",
            OriginalTitleNormalized = "overseas only",
            ReleaseYear = 2021,
            MetadataStatus = MetadataStatuses.Ready,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Titles.Add(overseas);
        await db.SaveChangesAsync();
        overseas.Localizations.Add(new TitleLocalization
        {
            TitleId = overseas.Id,
            Language = "en-US",
            Name = "Only Overseas",
            NameNormalized = "only overseas",
            FetchedAt = DateTime.UtcNow,
        });
        db.Availabilities.Add(new Availability
        {
            TitleId = overseas.Id,
            CatalogSourceId = unitedStates.Id,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            AddedAt = DateTime.UtcNow,
            IsBaseline = false,
        });
        var profile = new Profile { Name = "Overseas", Color = Profile.DefaultColor, CreatedAt = DateTime.UtcNow };
        db.Favorites.Add(new Favorite
        {
            Profile = profile,
            TitleId = overseas.Id,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var settings = await ConfigureArrAsync(sonarr: false, radarr: true);
        var sut = new TitleQueryService(db, settings, new FixedClock(DateTime.UtcNow));

        var listed = await sut.QueryTitlesAsync(italy.Id, new TitleQueryRequest(Favorite: true, IncludeRemoved: true));
        listed.Items.ShouldContain(i => i.Id == overseas.Id);
        listed.Items.Single(i => i.Id == overseas.Id).Places.ShouldContain(p => p.CountryCode == "US");

        var found = await sut.QueryFavoritesAsync(profile.Id, new TitleQueryRequest(Q: "overseas"));
        found.Items.ShouldContain(i => i.Id == overseas.Id);
        found.Items.Single(i => i.Id == overseas.Id).Places.ShouldContain(p => p.CountryCode == "US");

        var catalog = await sut.QueryTitlesAsync(italy.Id, new TitleQueryRequest(Q: "overseas"));
        catalog.Total.ShouldBe(0);

        var types = await sut.ListFavoriteTypesAsync(profile.Id);
        types.ShouldContain(t => t.Type == "movie");
    }

    [Fact]
    public async Task Types_include_standup_and_genre_filter_narrows_the_catalog()
    {
        await using var db = await CreateDbAsync();
        var source = await SeedCatalogAsync(db);
        var titles = await SeedTitlesAsync(db, source.Id);
        var genre = new Genre();
        db.Genres.Add(genre);
        await db.SaveChangesAsync();
        db.GenreLocalizations.Add(new GenreLocalization
        {
            GenreId = genre.Id,
            Language = "it-IT",
            Name = "Azione",
        });
        db.TitleGenres.Add(new TitleGenre { TitleId = titles.Movie.Id, GenreId = genre.Id });
        await db.SaveChangesAsync();

        var settings = await ConfigureArrAsync(sonarr: true, radarr: true);
        var sut = new TitleQueryService(db, settings, new FixedClock(new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc)));

        var types = await sut.ListTypesAsync(source.Id, favoritesOnly: false);
        types.Select(t => t.Type).ShouldBe(["movie", "series", "standup"]);

        var genres = await sut.ListGenresAsync(source.Id, favoritesOnly: false);
        genres.ShouldContain(g => g.Id == genre.Id && g.Name == "Azione");

        var filtered = await sut.QueryTitlesAsync(source.Id, new TitleQueryRequest(Genres: [genre.Id]));
        filtered.Items.Select(i => i.Id).ShouldBe([titles.Movie.Id]);

        var oldestFirst = await sut.QueryTitlesAsync(source.Id, new TitleQueryRequest(Sort: "year", Dir: "asc"));
        oldestFirst.Items.Select(i => i.Year).ShouldBe([1999, 2020, 2024]);
    }

    [Fact]
    public async Task Enabled_types_and_genres_union_enabled_sources_only()
    {
        await using var db = await CreateDbAsync();
        var enabled = await SeedCatalogAsync(db);
        var enabledTitles = await SeedTitlesAsync(db, enabled.Id);

        var disabledProvider = new Provider
        {
            Key = "prime",
            DisplayName = "Prime Video",
            ProviderType = "watchmode",
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.Providers.Add(disabledProvider);
        await db.SaveChangesAsync();
        var disabled = new CatalogSource
        {
            ProviderId = disabledProvider.Id,
            CountryCode = "US",
            MetadataLanguage = "en-US",
            IsEnabled = false,
            BaselineCompletedAt = DateTime.UtcNow,
            LastFullAt = DateTime.UtcNow,
        };
        db.CatalogSources.Add(disabled);
        await db.SaveChangesAsync();

        var now = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var disabledOnly = new Title
        {
            MediaType = MediaTypes.Movie,
            Category = TitleCategories.Movie,
            OriginalTitle = "Disabled Only",
            OriginalTitleNormalized = "disabled only",
            ReleaseYear = 2021,
            MetadataStatus = MetadataStatuses.Ready,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Titles.Add(disabledOnly);
        await db.SaveChangesAsync();
        var orphanGenre = new Genre { Id = 9001 };
        var sharedGenre = new Genre { Id = 9002 };
        db.Genres.AddRange(orphanGenre, sharedGenre);
        await db.SaveChangesAsync();
        db.GenreLocalizations.AddRange(
            new GenreLocalization
            {
                GenreId = orphanGenre.Id,
                Language = "en-US",
                Name = "Orphan",
            },
            new GenreLocalization
            {
                GenreId = sharedGenre.Id,
                Language = "it-IT",
                Name = "Dramma",
            });
        db.TitleGenres.AddRange(
            new TitleGenre { TitleId = disabledOnly.Id, GenreId = orphanGenre.Id },
            new TitleGenre { TitleId = enabledTitles.Movie.Id, GenreId = sharedGenre.Id });
        db.Availabilities.Add(new Availability
        {
            TitleId = disabledOnly.Id,
            CatalogSourceId = disabled.Id,
            FirstSeenAt = now,
            LastSeenAt = now,
            AddedAt = now,
            IsBaseline = false,
        });
        await db.SaveChangesAsync();

        var settings = await ConfigureArrAsync(sonarr: true, radarr: true);
        var sut = new TitleQueryService(db, settings, new FixedClock(now));

        var types = await sut.ListEnabledTypesAsync();
        types.Select(t => t.Type).ShouldBe(["movie", "series", "standup"]);

        var genres = await sut.ListEnabledGenresAsync();
        genres.ShouldContain(g => g.Id == sharedGenre.Id && g.Name == "Dramma");
        genres.ShouldNotContain(g => g.Id == orphanGenre.Id);
    }

    [Fact]
    public async Task FavoriteService_add_remove_is_idempotent()
    {
        await using var db = await CreateDbAsync();
        var source = await SeedCatalogAsync(db);
        var titles = await SeedTitlesAsync(db, source.Id);
        var profile = new Profile { Name = "Toggle", Color = Profile.DefaultColor, CreatedAt = DateTime.UtcNow };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        var favorites = new FavoriteService(db, new FixedClock(DateTime.UtcNow));

        await favorites.AddAsync(profile.Id, titles.Movie.Id);
        await favorites.AddAsync(profile.Id, titles.Movie.Id);
        (await db.Favorites.CountAsync()).ShouldBe(1);

        await favorites.RemoveAsync(profile.Id, titles.Movie.Id);
        await favorites.RemoveAsync(profile.Id, titles.Movie.Id);
        (await db.Favorites.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Rich_household_pages_stay_under_300ms_at_10000_titles()
    {
        await using var db = await CreateDbAsync();
        var source = await SeedCatalogAsync(db);
        var now = DateTime.UtcNow;
        var profiles = Enumerable.Range(1, 10).Select(i => new Profile
        {
            Name = $"Profile {i}",
            Color = Profile.DefaultColor,
            IsAdmin = i == 1,
            CreatedAt = now,
        }).ToList();
        var sources = new[] { "US", "GB", "DE", "FR" }.Select(country => new CatalogSource
        {
            ProviderId = source.ProviderId,
            CountryCode = country,
            MetadataLanguage = "en-US",
            IsEnabled = true,
        }).Prepend(source).ToList();
        var genres = Enumerable.Range(1, 6).Select(id => new Genre
        {
            Id = id,
            Localizations = new[] { "it-IT", "en-US" }.Select(language => new GenreLocalization
            {
                GenreId = id,
                Language = language,
                Name = $"Genre {id}",
            }).ToList(),
        }).ToList();
        db.Profiles.AddRange(profiles);
        db.CatalogSources.AddRange(sources.Skip(1));
        db.Genres.AddRange(genres);
        var titles = Enumerable.Range(1, 10000).Select(i => new Title
        {
            OriginalTitle = $"Title {i}",
            OriginalTitleNormalized = $"title {i}",
            MediaType = MediaTypes.Movie,
            Category = TitleCategories.Movie,
            ReleaseYear = 2020,
            MetadataStatus = MetadataStatuses.Ready,
            CreatedAt = now,
            UpdatedAt = now,
        }).ToList();
        db.Titles.AddRange(titles);
        await db.SaveChangesAsync();
        db.Availabilities.AddRange(titles.Select(t => new Availability
        {
            TitleId = t.Id,
            CatalogSourceId = source.Id,
            FirstSeenAt = now,
            LastSeenAt = now,
            AddedAt = now,
        }));
        foreach (var title in titles.Take(48))
        {
            foreach (var language in new[] { "it-IT", "en-US", "fr-FR", "de-DE", "es-ES" })
            {
                title.Localizations.Add(new TitleLocalization
                {
                    Language = language,
                    Name = title.OriginalTitle!,
                    NameNormalized = title.OriginalTitleNormalized!,
                    Overview = new string('x', 1000),
                    FetchedAt = now,
                });
            }
            foreach (var genre in genres) title.TitleGenres.Add(new TitleGenre { GenreId = genre.Id });
            foreach (var profile in profiles.Take(9))
            {
                title.Favorites.Add(new Favorite { ProfileId = profile.Id, CreatedAt = now });
                title.WatchedItems.Add(new Watched { ProfileId = profile.Id, WatchedAt = now });
                title.Ratings.Add(new Rating { ProfileId = profile.Id, Value = RatingValues.Like, RatedAt = now });
            }
            foreach (var extraSource in sources.Skip(1))
            {
                title.Availabilities.Add(new Availability
                {
                    CatalogSourceId = extraSource.Id,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    AddedAt = now,
                });
            }
        }
        db.RecoStates.Add(new RecoState { ProfileId = profiles[9].Id, CurrentVersion = 1, Stage = RecoLearning.StageCold });
        db.Recommendations.AddRange(titles.Select((title, index) => new Recommendation
        {
            ProfileId = profiles[9].Id,
            Version = 1,
            TitleId = title.Id,
            Rank = index + 1,
            Score = 1,
            Prior = 1,
        }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var settings = new MemorySettingsStore();
        await settings.SetAsync(SettingsKeys.RecoExploreRate, "0", false);
        var catalog = new TitleQueryService(db, settings, new FixedClock(now));
        var reco = new RecoQueryService(db, settings, new FixedClock(now));
        var request = new TitleQueryRequest(Q: "title", Sort: "year", Dir: "asc");
        var recommendationRequest = new RecommendationsQuery(SourceId: source.Id, Scope: RecoMembership.ScopeSource,
            Diversify: false, ProfileId: profiles[9].Id);

        // Warm EF query compilation separately from the steady-state read budget.
        await catalog.QueryTitlesAsync(source.Id, request, personalProfileId: profiles[0].Id);
        await catalog.GetTitleAsync(titles[0].Id, source.Id, personalProfileId: profiles[0].Id);
        await reco.QueryAsync(recommendationRequest);
        var timings = new Dictionary<string, List<double>>
        {
            ["catalog/search"] = [],
            ["detail"] = [],
            ["recommendations"] = [],
        };
        for (var sample = 0; sample < 10; sample++)
        {
            var timer = Stopwatch.StartNew();
            var page = await catalog.QueryTitlesAsync(source.Id, request, personalProfileId: profiles[0].Id);
            timings["catalog/search"].Add(timer.Elapsed.TotalMilliseconds);
            page.Total.ShouldBe(10000);
            page.Items.Count.ShouldBe(48);
            page.Items[0].Genres.Count.ShouldBe(6);
            page.Items[0].Places!.Count.ShouldBe(5);
            page.Items[0].Rating.ShouldBe("like");
            page.Items[0].IsFavorite.ShouldBeTrue();
            page.Items[0].IsWatched.ShouldBeTrue();

            timer.Restart();
            var detail = await catalog.GetTitleAsync(titles[0].Id, source.Id, personalProfileId: profiles[0].Id);
            timings["detail"].Add(timer.Elapsed.TotalMilliseconds);
            detail!.Availability.Count.ShouldBe(5);
            detail!.Rating.ShouldBe("like");

            timer.Restart();
            var recommendations = await reco.QueryAsync(recommendationRequest);
            timings["recommendations"].Add(timer.Elapsed.TotalMilliseconds);
            recommendations.Total.ShouldBe(10000);
            recommendations.Items.Count.ShouldBe(48);
            recommendations.Items[0].Rating.ShouldBeNull();
            recommendations.Items[0].IsFavorite.ShouldBeFalse();
        }
        foreach (var (name, samples) in timings)
        {
            var p95 = samples.Order().ElementAt((int)Math.Ceiling(samples.Count * 0.95) - 1);
            output.WriteLine($"{name}: p95 {p95:F1} ms (10k titles, 10 profiles, 48 rich cards)");
            p95.ShouldBeLessThan(300, $"{name} exceeds the NFR-01/NFR-REC-01 read budget");
        }
    }

    private static async Task<MemorySettingsStore> ConfigureArrAsync(bool sonarr, bool radarr)
    {
        var settings = new MemorySettingsStore();
        if (sonarr)
        {
            await settings.SetAsync(SettingsKeys.SonarrUrl, JsonSerializer.Serialize("http://sonarr"), false);
            await settings.SetAsync(SettingsKeys.SonarrApiKey, JsonSerializer.Serialize("key"), true);
        }

        if (radarr)
        {
            await settings.SetAsync(SettingsKeys.RadarrUrl, JsonSerializer.Serialize("http://radarr"), false);
            await settings.SetAsync(SettingsKeys.RadarrApiKey, JsonSerializer.Serialize("key"), true);
        }

        return settings;
    }

    private static async Task<PickarrDbContext> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PickarrDbContext>().UseSqlite(connection)
            .ConfigureWarnings(warnings => warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning)).Options;
        var db = new PickarrDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static async Task<CatalogSource> SeedCatalogAsync(PickarrDbContext db)
    {
        var provider = new Provider
        {
            Key = "netflix",
            DisplayName = "Netflix",
            ProviderType = "watchmode",
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.Providers.Add(provider);
        await db.SaveChangesAsync();
        var source = new CatalogSource
        {
            ProviderId = provider.Id,
            CountryCode = "IT",
            MetadataLanguage = "it-IT",
            IsEnabled = true,
            BaselineCompletedAt = DateTime.UtcNow,
            LastFullAt = DateTime.UtcNow,
        };
        db.CatalogSources.Add(source);
        await db.SaveChangesAsync();
        return source;
    }

    private sealed record SeededTitles(Title Movie, Title Series, Title StandUp);

    private static async Task<SeededTitles> SeedTitlesAsync(PickarrDbContext db, int sourceId)
    {
        var now = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var movie = new Title
        {
            MediaType = MediaTypes.Movie,
            Category = TitleCategories.Movie,
            OriginalTitle = "The Matrix",
            OriginalTitleNormalized = TextNormalizer.NormalizeName("The Matrix"),
            ReleaseYear = 1999,
            VoteTmdb = 8.1,
            TmdbId = 603,
            TmdbType = "movie",
            MetadataStatus = MetadataStatuses.Ready,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var series = new Title
        {
            MediaType = MediaTypes.Series,
            Category = TitleCategories.Series,
            OriginalTitle = "Example Series",
            OriginalTitleNormalized = "example series",
            ReleaseYear = 2020,
            TvdbId = 121361,
            TmdbId = 1396,
            TmdbType = "tv",
            MetadataStatus = MetadataStatuses.Ready,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var standup = new Title
        {
            MediaType = MediaTypes.Series,
            Category = TitleCategories.StandUp,
            OriginalTitle = "Stand Up Special",
            OriginalTitleNormalized = "stand up special",
            ReleaseYear = 2024,
            MetadataStatus = MetadataStatuses.Ready,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Titles.AddRange(movie, series, standup);
        await db.SaveChangesAsync();

        movie.Localizations.Add(new TitleLocalization
        {
            TitleId = movie.Id,
            Language = "it-IT",
            Name = "Caffè Latte Movie",
            NameNormalized = TextNormalizer.NormalizeName("Caffè Latte Movie"),
            Overview = "x",
            FetchedAt = now,
        });
        series.Localizations.Add(new TitleLocalization
        {
            TitleId = series.Id,
            Language = "it-IT",
            Name = "Serie Esempio",
            NameNormalized = "serie esempio",
            FetchedAt = now,
        });
        standup.Localizations.Add(new TitleLocalization
        {
            TitleId = standup.Id,
            Language = "it-IT",
            Name = "Special Stand-up",
            NameNormalized = "special stand-up",
            FetchedAt = now,
        });

        db.Availabilities.AddRange(
            new Availability
            {
                TitleId = movie.Id,
                CatalogSourceId = sourceId,
                FirstSeenAt = now,
                LastSeenAt = now,
                AddedAt = now,
                IsBaseline = false,
            },
            new Availability
            {
                TitleId = series.Id,
                CatalogSourceId = sourceId,
                FirstSeenAt = now,
                LastSeenAt = now,
                AddedAt = now,
                IsBaseline = false,
            },
            new Availability
            {
                TitleId = standup.Id,
                CatalogSourceId = sourceId,
                FirstSeenAt = now,
                LastSeenAt = now,
                AddedAt = now,
                IsBaseline = false,
            });
        await db.SaveChangesAsync();
        return new SeededTitles(movie, series, standup);
    }
}
