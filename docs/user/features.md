# Features

## Catalog

Pickarr syncs **catalog sources**: each source is a streaming **provider + country** pair (for example Netflix IT). Offered provider/country pairs are seeded disabled; you enable them in the first-run **Setup** wizard or later in Settings → Catalogs, within Watchmode plan limits.

On `/catalog`:

- Choose one provider, then one country.
- Quick filters: **All**, **New**, **Leaving**, **Recently removed**, **In library**.
- **In library** shows only titles whose cached Sonarr/Radarr status is **Downloaded**, within the selected provider and country. Missing, downloading, and partial titles are excluded.
- The search field has a **Clear search** button when text is entered; clearing it keeps the other filters.
- Filters: type, genre, year, library state, sort, include removed.
- Infinite poster grid with library badges, favorite heart, and rating controls.
- With recommendations enabled, the **For you** row under **All** (without search) follows the selected provider, country, type, and genre.
- The preferred source is remembered per profile (`pickarr.sourceId.{profileId}`).

Type and genre options come from titles in the current scope. Sonarr and Radarr are optional: the catalog stays browsable without them. Library actions appear only when the matching instance is configured (series → Sonarr; movies → Radarr).

### Sync behavior (operator view)

- Scheduled incremental and full sync jobs keep the local catalog current.
- On the Watchmode **free** plan, change feeds and a full leaving calendar are not available: incremental sync uses recent list pages; removals are confirmed across full syncs; leaving dates are a short global preview (about 10 titles across your free-plan countries, not per catalog). The leaving sync upserts those titles onto matching enabled sources so they can appear under **Leaving** even before a full catalog baseline. Older catalog titles (for example a series whose last “new” release is a new season) usually appear under **All** only after a **full sync** completes and sets the source baseline.
- Catalog search is scoped to the selected provider + country. A title present on Netflix IT does not appear under Netflix US until that source has an active availability row.
- Free plan limits: about 2 500 credits/month and at most **3** distinct countries (several providers in one country count once).
- Cached catalog data should be refreshed within Watchmode’s free-plan cache window; the UI warns when a source looks stale.
- Titles imported with an IMDb id but no TMDB id stay in the enrichment queue (`Pending`) so TMDB `/find` can fill posters and metadata; duplicate Watchmode rows that share an IMDb id are merged into one title.
- Detailed cast is filled asynchronously from TMDB. Existing names remain visible while a title is waiting for its first cast fetch; an explicitly fetched empty cast is shown as empty rather than restoring stale names. The hourly cast job processes bounded batches and refreshes cast after 30 days.

## Title detail

Opening a title keeps you on the route that opened it. The detail dialog shows:

- Metadata (overview, genres, scores, external links)
- Up to 12 cast members in a horizontally scrollable row, with TMDB photos or accessible placeholders and IMDb links when available
- Availability on the current source (added / leaving / removed)
- Compact shared Arr library status (monitor and download progress), with expandable library season details for series
- Open an existing library title in Sonarr or Radarr in a new tab when its title slug is available. Existing entries acquire their slug at the next library sync or status refresh; the configured Arr URL must be reachable from your browser.
- Actions: favorite, rate, Add / Delete, Search now, Refresh status
- When recommendations are enabled, select **Show similar titles** to start a search. A spinner shows that the search is running; opening a title does not start it automatically.
- Series seasons are newest first; episodes oldest first. Adding a series sends only selected seasons.

## Profiles (household)

Up to **10** profiles share one install. There is no authentication: profiles are a logical partition of taste and lists.

| Rule | Behavior |
| --- | --- |
| Admin | First profile is admin (`IsAdmin`). Role is not transferred; admin profile is not deleted. |
| Who | `/who` selects the active profile. The current profile is marked in the picker and shown beside the header avatar. Last id is stored in `localStorage` as `pickarr.profileId`. |
| Single profile | One profile skips the picker and enters the catalog. |
| Header | Profile control always opens `/who`. After choice the SPA sends `X-Pickarr-Profile`. |
| Settings | Non-admin profiles do not see Settings; `/settings/*` redirects to the catalog. Settings APIs stay open. |
| User management | Admins manage household profiles in Settings → Users. `/who` remains the profile picker; each profile can edit its own name, color, and recommendation preferences there. |
| Personal data | Favorites, ratings, watched/hide, recommendations, and reco toggles are per profile. |
| Shared data | Catalog metadata, embeddings, app settings, and Arr library rows stay global. |

Anyone can edit their own name, color, and reco toggles (`useImplicit`, `excludeInLibrary`). Only the admin creates or deletes profiles and can edit every profile from Settings → Users.

## My list

`/list` is the active profile’s favorites across providers and countries. It is **not** a catalog filter (`view=favorites` / `favorite=true` on catalog are ignored; `/catalog?view=favorites` redirects to `/list`).

## Ratings and For you

Explicit votes on a title (per profile): **Love**, **Like**, **Dislike**, **Not interested**. **Hide** (watched) is separate and excludes from For you by default without adding taste weight.

Votes are highlighted immediately. While a vote is saving, that title's rating controls are temporarily disabled, including in an open detail dialog. Failed saves restore the previous vote and show an error so you can retry. Recommended cards stay visible while saving, then briefly show the saved vote before fading out.

**For you** (`/for-you`) shows local recommendations over titles already in enabled catalogs. Your profile can still learn from votes on titles outside those catalogs when Pickarr has matching metadata or a usable local embedding. Those titles do not become recommendations.

- Default filter: **All** providers and countries (union of enabled sources). Provider and country each offer an All option so the cross-catalog view is explicit.
- Choosing a concrete provider + country narrows the list to that source immediately (no search box on this page).
- Type and genre filters work with All selected (options from the enabled-catalog union) or with a concrete source (options from that source).
- Cold start: while effective ratings are below the personalization threshold (`N0`, default 30), recommendations can blend a **household prior** from other profiles’ Love/Like (capped); at full personalization that term is zero
- Optional ONNX embedding model under the data path improves similarity when installed
- Taste data never leaves the container

`/ratings` lists votes with export/import. Provider/country default to **All** (ratings stay global); a selected source only drives presence badges. `/taste` helps teach taste while learning is cold.

## Sonarr / Radarr

- Optional. Configure later or never; without Arr, Pickarr remains a catalog browser and recommendation engine.
- When configured, add series to Sonarr and movies to Radarr from the title dialog. The library section is omitted for a media type whose instance is not configured.
- While an add request is running, the dialog shows an adding indicator and disables Add and Close until the request succeeds or fails. Errors leave the dialog open for retry.
- See shared library status on cards and in detail when the matching Arr is configured.
- For a series already in Sonarr, use the season table in its detail dialog to enable or stop monitoring individual seasons.
- Delete with confirmation; destructive delete options default off.
- Periodic Arr sync (and optional webhooks when configured) refreshes status.
- An Arr add with `X-Pickarr-Profile` stores the profile on the audit row for that profile’s implicit signal; without the header the add is still valid but not attributed for taste.
- When sync or refresh observes a completed download after that profile's add, it records a persistent positive signal. The default add (+0.6) and download (+0.4) weights sum to +1. Deleting the title or its files later does not remove either signal. Explicit ratings take precedence; disabling implicit signals ignores both.
- Downloads already deleted before Pickarr observed their completion cannot be reconstructed. Existing cached completed downloads are recorded during the next successful sync/refresh or before a deletion.

## First-run setup

Until `setup.completed` is set, every route redirects to `/setup`. The wizard is blocking and saves each step. It covers: admin name/color; Watchmode and TMDB keys (required, with connection tests), Watchmode plan, recommendations toggle; at least one catalog provider+country; optional Sonarr/Radarr (test required when filled). Finish sets the flag and queues the first catalog import. Reserve percent and max credits per run stay under Settings → Connections → Watchmode → Advanced (defaults are fine for most installs).

## Administration

Admin-only Settings tabs are ordered System, Users, Catalogs, Recommendations, Connections, and Jobs. They cover system stats, profile management, catalog sources, recommendation settings (model status, rebuild/eval/reset), Sonarr/Radarr and Watchmode/TMDB connections, and job control. Job views show runs (with kind-specific stats), Watchmode credit usage, and unmapped titles; the Jobs page also configures how many finished runs to keep per job. System shows the running image version and, when GHCR has a numerically newer version tag, shows only the newest one (release builds only consider release tags; development builds consider any versioned tag). System also lists log files for download and can clear them.

## Attribution

UI includes attribution for Watchmode and TMDB as required by their personal / non-commercial API terms.
