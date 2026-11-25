using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pickarr.Core.Abstractions;
using Pickarr.Core.Arr;
using Pickarr.Core.Domain;
using Pickarr.Core.Domain.Entities;
using Pickarr.Core.Settings;
using Pickarr.Infrastructure.Arr;
using Pickarr.Infrastructure.Catalog;
using Pickarr.Infrastructure.Persistence;
using Pickarr.Infrastructure.Time;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Pickarr.Infrastructure.Tests;

public sealed class ArrClientAndSyncTests : IAsyncLifetime
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly string _fixtureRoot = FindFixtureRoot();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _server.Stop();
        _server.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SonarrClient_probe_returns_profiles_for_the_credentials_under_test()
    {
        _server.Reset();
        MapFixture("/api/v3/system/status", "sonarr/system-status.json");
        MapFixture("/api/v3/qualityprofile", "sonarr/qualityprofiles.json");
        MapFixture("/api/v3/rootfolder", "sonarr/rootfolders.json");

        var http = new HttpClient { BaseAddress = new Uri(_server.Urls[0]) };
        var client = new SonarrClient(http, new MemorySettingsStore(), NullLogger<SonarrClient>.Instance);

        var probe = await client.ProbeAsync(_server.Urls[0], "one-off-key");

        probe.Version.ShouldContain("4.");
        probe.AppName.ShouldBe("Sonarr");
        probe.QualityProfiles.Select(p => p.Name).ShouldBe(["Any", "HD-1080p"]);
        probe.RootFolders.Single().Path.ShouldBe("/tv");
    }

    [Fact]
    public async Task RadarrClient_probe_returns_profiles_for_the_credentials_under_test()
    {
        _server.Reset();
        MapFixture("/api/v3/system/status", "radarr/system-status.json");
        MapFixture("/api/v3/qualityprofile", "radarr/qualityprofiles.json");
        MapFixture("/api/v3/rootfolder", "radarr/rootfolders.json");

        var http = new HttpClient { BaseAddress = new Uri(_server.Urls[0]) };
        var client = new RadarrClient(http, new MemorySettingsStore(), NullLogger<RadarrClient>.Instance);

        var probe = await client.ProbeAsync(_server.Urls[0], "one-off-key");

        probe.AppName.ShouldBe("Radarr");
        probe.QualityProfiles.ShouldContain(p => p.Name == "HD-1080p");
        probe.RootFolders.Single().Path.ShouldBe("/movies");
    }

    [Fact]
    public async Task SonarrClient_parses_library_and_queue_fixtures()
    {
        MapFixture("/api/v3/system/status", "sonarr/system-status.json");
        MapFixture("/api/v3/series", "sonarr/series-library.json");
        MapFixture("/api/v3/queue", "sonarr/queue-page1.json");
        MapFixture("/api/v3/qualityprofile", "sonarr/qualityprofiles.json");
        MapFixture("/api/v3/rootfolder", "sonarr/rootfolders.json");
        MapJson("/api/v3/tag", "[]");

        var settings = CreateSettings(sonarr: true);
        var http = new HttpClient { BaseAddress = new Uri(_server.Urls[0]) };
        var client = new SonarrClient(http, settings, NullLogger<SonarrClient>.Instance);

        var info = await client.GetSystemInfoAsync();
        info.Version.ShouldContain("4.");
        var library = await client.GetLibraryAsync();
        library.Count.ShouldBe(2);
        library[0].TitleSlug.ShouldBe("example-series");
        library[0].Seasons.Count.ShouldBe(3);
        var queue = await client.GetQueueAsync();
        queue.ShouldContain(q => q.ArrItemId == 55);
        var options = await client.GetOptionsAsync();
        options.DefaultQualityProfileId.ShouldBe(1);
        options.DefaultRootFolderPath.ShouldBe("/tv");
    }

    [Fact]
    public async Task ArrLibrarySync_upserts_and_keeps_cache_on_failure()
    {
        MapFixture("/api/v3/series", "sonarr/series-library.json");
        MapFixture("/api/v3/queue", "sonarr/queue-page1.json");

        await using var db = await CreateDbAsync();
        var settings = CreateSettings(sonarr: true);
        db.Titles.Add(new Title
        {
            MediaType = MediaTypes.Series,
            Category = TitleCategories.Series,
            TvdbId = 121361,
            TmdbId = 1396,
            TmdbType = "tv",
            OriginalTitle = "Example Series",
            MetadataStatus = MetadataStatuses.Ready,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var http = new HttpClient { BaseAddress = new Uri(_server.Urls[0]) };
        var sonarr = new SonarrClient(http, settings, NullLogger<SonarrClient>.Instance);
        var radarr = new RadarrClient(http, settings, NullLogger<RadarrClient>.Instance);
        var factory = new ArrClientFactory(sonarr, radarr, settings);
        var syncRuns = new EfSyncRunStore(db, new SystemClock(), new TestSettingsStore());
        var sync = new ArrLibrarySync(db, factory, new SystemClock(), syncRuns, NullLogger<ArrLibrarySync>.Instance);

        await sync.SyncAsync(ArrKind.Sonarr);
        (await db.ArrLibraryItems.CountAsync()).ShouldBe(2);
        var matched = await db.ArrLibraryItems.SingleAsync(a => a.ArrId == 55);
        matched.TitleId.ShouldNotBeNull();
        matched.TitleSlug.ShouldBe("example-series");
        matched.State.ShouldBe(ArrStates.Downloading);

        _server.Reset();
        _server
            .Given(Request.Create().WithPath("/api/v3/series").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        await Should.ThrowAsync<ArrClientException>(() => sync.SyncAsync(ArrKind.Sonarr));
        (await db.ArrLibraryItems.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task RadarrClient_parses_title_slug()
    {
        MapFixture("/api/v3/movie", "radarr/movie-library.json");
        var client = new RadarrClient(new HttpClient(), CreateSettings(sonarr: false, radarr: true), NullLogger<RadarrClient>.Instance);

        var library = await client.GetLibraryAsync();

        library[0].TitleSlug.ShouldBe("the-matrix-1999");
    }

    [Theory]
    [InlineData(ArrKind.Radarr)]
    [InlineData(ArrKind.Sonarr)]
    public async Task Completed_download_signal_is_profile_scoped_idempotent_and_survives_removal(ArrKind kind)
    {
        await using var db = await CreateDbAsync();
        var now = DateTime.UtcNow;
        var title = new Title
        {
            MediaType = kind == ArrKind.Radarr ? MediaTypes.Movie : MediaTypes.Series,
            Category = kind == ArrKind.Radarr ? TitleCategories.Movie : TitleCategories.Series,
            TmdbType = kind == ArrKind.Radarr ? "movie" : "tv",
            TmdbId = 603,
            MetadataStatus = MetadataStatuses.Ready,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var owner = new Profile { Name = "Owner", Color = Profile.DefaultColor, CreatedAt = now };
        var other = new Profile { Name = "Other", Color = Profile.DefaultColor, CreatedAt = now };
        db.Titles.Add(title);
        db.Profiles.AddRange(owner, other);
        await db.SaveChangesAsync();
        foreach (var (profileId, outcome) in new (int?, string)[]
                 { (owner.Id, ArrAuditOutcomes.Success), (other.Id, ArrAuditOutcomes.Failed), (null, ArrAuditOutcomes.Success) })
        {
            db.ArrAuditLogs.Add(new ArrAuditLog
            {
                ProfileId = profileId,
                TitleId = title.Id,
                ArrId = 10,
                Kind = kind.ToStorage(),
                At = now,
                Action = ArrAuditActions.Add,
                Outcome = outcome,
                Options = "{}",
            });
        }
        await db.SaveChangesAsync();

        var settings = CreateSettings(sonarr: true, radarr: true);
        var http = new HttpClient { BaseAddress = new Uri(_server.Urls[0]) };
        var factory = new ArrClientFactory(
            new SonarrClient(http, settings, NullLogger<SonarrClient>.Instance),
            new RadarrClient(http, settings, NullLogger<RadarrClient>.Instance), settings);
        var sync = new ArrLibrarySync(db, factory, new SystemClock(),
            new EfSyncRunStore(db, new SystemClock(), new TestSettingsStore()), NullLogger<ArrLibrarySync>.Instance);
        var item = new ArrItemSnapshot(10, "Downloaded", 2020, 603, null, null, true, null, 100,
            true, true, 8, 8, 8, [new ArrSeasonSnapshot(1, true, 8, 8, 8, 100)], "downloaded");

        await sync.UpsertItemAsync(kind, item);
        await sync.UpsertItemAsync(kind, item);
        (await db.ArrLibraryItems.SingleAsync()).TitleSlug.ShouldBe("downloaded");
        var download = await db.ArrAuditLogs.SingleAsync(a => a.Action == ArrAuditActions.Download);
        download.ProfileId.ShouldBe(owner.Id);
        download.TitleId.ShouldBe(title.Id);
        download.Outcome.ShouldBe(ArrAuditOutcomes.Success);

        // Later missing files must not erase an observed completion.
        await sync.UpsertItemAsync(kind, item with
        {
            HasFile = false,
            Seasons = [new ArrSeasonSnapshot(1, true, 0, 8, 8, 0)],
        });
        await sync.RemoveItemAsync(kind, item.ArrId);
        (await db.ArrLibraryItems.CountAsync()).ShouldBe(0);
        (await db.ArrAuditLogs.CountAsync(a => a.Action == ArrAuditActions.Download)).ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cached_download_is_preserved_before_explicit_or_external_deletion(bool externalDeletion)
    {
        await using var db = await CreateDbAsync();
        var now = DateTime.UtcNow;
        var owner = new Profile { Name = "Owner", Color = Profile.DefaultColor, CreatedAt = now };
        db.Profiles.Add(owner);
        await db.SaveChangesAsync();
        db.ArrAuditLogs.Add(new ArrAuditLog
        {
            ProfileId = owner.Id,
            TitleId = 42,
            ArrId = 10,
            Kind = ArrKinds.Radarr,
            At = now,
            Action = ArrAuditActions.Add,
            Outcome = ArrAuditOutcomes.Success,
            Options = "{}",
        });
        db.ArrLibraryItems.Add(new ArrLibraryItem
        {
            TitleId = 42,
            ArrId = 10,
            Kind = ArrKinds.Radarr,
            ArrTitle = "Cached",
            State = ArrStates.Downloaded,
            SyncedAt = now,
        });
        db.Titles.Add(new Title
        {
            Id = 42,
            MediaType = MediaTypes.Movie,
            Category = TitleCategories.Movie,
            MetadataStatus = MetadataStatuses.Ready,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        MapJson("/api/v3/movie", "[]");
        MapJson("/api/v3/queue", "{\"records\":[],\"totalRecords\":0}");
        var settings = CreateSettings(sonarr: false, radarr: true);
        var http = new HttpClient { BaseAddress = new Uri(_server.Urls[0]) };
        var factory = new ArrClientFactory(
            new SonarrClient(http, settings, NullLogger<SonarrClient>.Instance),
            new RadarrClient(http, settings, NullLogger<RadarrClient>.Instance), settings);
        var sync = new ArrLibrarySync(db, factory, new SystemClock(),
            new EfSyncRunStore(db, new SystemClock(), new TestSettingsStore()), NullLogger<ArrLibrarySync>.Instance);

        if (externalDeletion) await sync.SyncAsync(ArrKind.Radarr);
        else await sync.RemoveItemAsync(ArrKind.Radarr, 10);

        (await db.ArrLibraryItems.CountAsync()).ShouldBe(0);
        var download = await db.ArrAuditLogs.SingleAsync(a => a.Action == ArrAuditActions.Download);
        download.ProfileId.ShouldBe(owner.Id);
        download.TitleId.ShouldBe(42);
    }

    private MemorySettingsStore CreateSettings(bool sonarr, bool radarr = false)
    {
        var settings = new MemorySettingsStore();
        if (sonarr)
        {
            settings.Set(SettingsKeys.SonarrUrl, JsonSerializer.Serialize(_server.Urls[0]));
            settings.Set(SettingsKeys.SonarrApiKey, JsonSerializer.Serialize("key"), secret: true);
        }

        if (radarr)
        {
            settings.Set(SettingsKeys.RadarrUrl, JsonSerializer.Serialize(_server.Urls[0]));
            settings.Set(SettingsKeys.RadarrApiKey, JsonSerializer.Serialize("key"), secret: true);
        }

        return settings;
    }

    private void MapFixture(string path, string relative)
    {
        var body = File.ReadAllText(Path.Combine(_fixtureRoot, relative));
        MapJson(path, body);
    }

    private void MapJson(string path, string body)
    {
        _server
            .Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));
    }

    private static async Task<PickarrDbContext> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PickarrDbContext>().UseSqlite(connection).Options;
        var db = new PickarrDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class MemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, (string Json, bool Secret)> _values = new(StringComparer.Ordinal);

        public void Set(string key, string valueJson, bool secret = false) =>
            _values[key] = (valueJson, secret);

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

    private static string FindFixtureRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures");
            if (Directory.Exists(Path.Combine(candidate, "sonarr")))
            {
                return candidate;
            }

            candidate = Path.Combine(dir.FullName, "fixtures");
            if (Directory.Exists(Path.Combine(candidate, "sonarr")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("fixtures folder not found");
    }
}
