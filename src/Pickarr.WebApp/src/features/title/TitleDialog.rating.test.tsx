import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchTitle } from '../../api/catalog'
import { refreshTitleArr, setTitleSeasonMonitoring } from '../../api/arr'
import { setRating } from '../../api/ratings'
import { fetchSettings } from '../../api/settings'
import type { TitleDetail } from '../../api/types'
import { addWatched } from '../../api/watched'
import i18n from '../../i18n'
import { TitleDialog } from './TitleDialog'

vi.mock('../../api/catalog', () => ({
  fetchTitle: vi.fn(),
  fetchSeriesSeasons: vi.fn(async () => []),
}))

vi.mock('../../api/arr', () => ({
  refreshTitleArr: vi.fn(async () => null),
  setTitleSeasonMonitoring: vi.fn(async () => undefined),
}))

vi.mock('../../api/ratings', () => ({
  setRating: vi.fn(async () => undefined),
  clearRating: vi.fn(async () => undefined),
}))

vi.mock('../../api/watched', () => ({
  addWatched: vi.fn(async () => undefined),
  removeWatched: vi.fn(async () => undefined),
}))

vi.mock('../../api/settings', () => ({
  fetchSettings: vi.fn(),
}))

vi.mock('../reco/SimilarRow', () => ({
  SimilarRow: () => null,
}))

function renderDialog() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const ui: ReactNode = (
    <QueryClientProvider client={client}>
      <I18nextProvider i18n={i18n}>
        <MemoryRouter initialEntries={['/catalog?title=42']}>
          <Routes>
            <Route path="/catalog" element={<TitleDialog titleId={42} sourceId={1} />} />
          </Routes>
        </MemoryRouter>
      </I18nextProvider>
    </QueryClientProvider>
  )
  return render(ui)
}

describe('TitleDialog ratings', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    HTMLDialogElement.prototype.showModal = vi.fn(function showModal(this: HTMLDialogElement) {
      this.open = true
    })
    HTMLDialogElement.prototype.close = vi.fn(function close(this: HTMLDialogElement) {
      this.open = false
      this.dispatchEvent(new Event('close'))
    })
    vi.mocked(fetchSettings).mockResolvedValue({
      reco: { enabled: true },
    } as Awaited<ReturnType<typeof fetchSettings>>)
    vi.mocked(fetchTitle).mockResolvedValue({
      id: 42,
      name: 'Dialog Movie',
      originalTitle: 'Dialog Movie',
      year: 2021,
      category: 'movie',
      overview: null,
      runtimeMinutes: null,
      seasonCount: null,
      episodeCount: null,
      genres: [],
      posterUrl: null,
      backdropUrl: null,
      ratings: { tmdb: null, tmdbVotes: null },
      links: { tmdb: 'https://example.com/tmdb', imdb: 'https://example.com/imdb', tvdb: 'https://example.com/tvdb' },
      isFavorite: false,
      isWatched: false,
      rating: null,
      metadataStatus: 'ready',
      availability: [{ sourceId: 1, countryCode: 'IT', addedAt: null, removedAt: null, leavingDate: null }],
      arr: {
        kind: 'radarr',
        arrId: null,
        state: 'notInLibrary',
        canAdd: false,
        canDelete: false,
        addBlockedReason: 'notConfigured',
        monitored: null,
        progress: null,
        sizeOnDisk: null,
        path: null,
        fileCount: null,
        syncedAt: null,
        seasons: null,
        externalUrl: null,
      },
    } as TitleDetail)
  })

  it('hides the library section when Arr is not configured', async () => {
    renderDialog()

    expect(await screen.findByRole('heading', { name: 'Dialog Movie' })).toBeTruthy()
    expect(screen.queryByRole('heading', { name: 'Library' })).toBeNull()
    expect(screen.queryByText(/Arr instance not configured/i)).toBeNull()
  })

  it('places the library section below the personal actions', async () => {
    vi.mocked(fetchTitle).mockResolvedValueOnce({
      id: 42,
      name: 'Dialog Movie',
      originalTitle: 'Dialog Movie',
      year: 2021,
      category: 'movie',
      overview: null,
      runtimeMinutes: null,
      seasonCount: null,
      episodeCount: null,
      genres: [{ id: 1, name: 'Drama' }],
      posterUrl: null,
      backdropUrl: null,
      ratings: { tmdb: null, tmdbVotes: null },
      links: { tmdb: null, imdb: null, tvdb: null },
      isFavorite: false,
      isWatched: false,
      rating: null,
      metadataStatus: 'ready',
      availability: [],
      cast: [],
      arr: {
        kind: 'radarr',
        arrId: 7,
        state: 'downloaded',
        canAdd: false,
        canDelete: true,
        addBlockedReason: null,
        monitored: null,
        progress: null,
        sizeOnDisk: null,
        path: '/movies/dialog',
        fileCount: 1,
        syncedAt: '2026-11-25T08:00:00Z',
        seasons: null,
        externalUrl: 'http://radarr/base/movie/dialog-movie',
      },
    } as TitleDetail)

    renderDialog()

    const actions = await screen.findByRole('button', { name: 'Like' })
    const library = await screen.findByRole('heading', { name: 'Library' })
    expect(actions.compareDocumentPosition(library) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(screen.getByText('Genres: Drama')).toBeTruthy()
    const open = screen.getByRole('link', { name: 'Open in Radarr' })
    expect(open).toHaveAttribute('href', 'http://radarr/base/movie/dialog-movie')
    expect(open).toHaveAttribute('target', '_blank')
    expect(screen.getByRole('button', { name: 'Remove from library' }).parentElement).toContainElement(open)
  })

  it('keeps availability and external links in the poster column', async () => {
    renderDialog()

    await screen.findByRole('heading', { name: 'Dialog Movie' })
    const column = screen.getByRole('heading', { name: 'Availability' }).parentElement?.parentElement
    expect(column).toContainElement(screen.getByText(/IT: Available/))
    for (const name of ['TMDB', 'IMDb', 'TVDB']) {
      expect(column).toContainElement(screen.getByRole('link', { name }))
    }
  })

  it('keeps each availability date together with its status', async () => {
    const base = await vi.mocked(fetchTitle)(42)
    vi.mocked(fetchTitle).mockResolvedValue({
      ...base,
      availability: [
        { sourceId: 1, countryCode: 'IT', addedAt: '2026-11-10T12:00:00Z', removedAt: null, leavingDate: '2026-12-01' },
        { sourceId: 2, countryCode: 'US', addedAt: '2026-11-01T12:00:00Z', removedAt: '2026-11-20T12:00:00Z', leavingDate: null },
      ],
    })
    renderDialog()

    await screen.findByText(/IT: Available since/)
    expect(screen.getByText('Nov 10, 2026')).toHaveClass('whitespace-nowrap')
    expect(screen.getByText('2026-12-01')).toHaveClass('whitespace-nowrap')
    expect(screen.getByText(/US: Removed on/).textContent).toContain('Nov 20, 2026')
  })

  it('refreshes a missing Arr link before opening the title', async () => {
    const base = await vi.mocked(fetchTitle)(42)
    const arr = { ...base.arr, canDelete: true, addBlockedReason: 'alreadyInLibrary', arrId: 7 }
    vi.mocked(fetchTitle).mockResolvedValue({ ...base, arr })
    vi.mocked(refreshTitleArr).mockResolvedValue({ ...arr, externalUrl: 'http://radarr/movie/dialog' })
    const tab = { opener: null, close: vi.fn(), location: { replace: vi.fn() } }
    const open = vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window)
    const user = userEvent.setup()
    renderDialog()

    await user.click(await screen.findByRole('button', { name: 'Open in Radarr' }))
    await waitFor(() => expect(tab.location.replace).toHaveBeenCalledWith('http://radarr/movie/dialog'))
    expect(refreshTitleArr).toHaveBeenCalledWith(42)
    open.mockRestore()
  })

  it('reports an unavailable link after refreshing and closes the new tab', async () => {
    const base = await vi.mocked(fetchTitle)(42)
    vi.mocked(fetchTitle).mockResolvedValue({
      ...base,
      arr: { ...base.arr, canDelete: true, addBlockedReason: 'alreadyInLibrary', arrId: 7 },
    })
    vi.mocked(refreshTitleArr).mockResolvedValue(null)
    const tab = { opener: null, close: vi.fn() }
    const open = vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window)
    const user = userEvent.setup()
    renderDialog()

    await user.click(await screen.findByRole('button', { name: 'Open in Radarr' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('The title link is unavailable')
    expect(tab.close).toHaveBeenCalled()
    open.mockRestore()
  })

  it('collapses library season details without hiding monitoring or the Sonarr link', async () => {
    const base = await vi.mocked(fetchTitle)(42)
    vi.mocked(fetchTitle).mockResolvedValue({
      ...base,
      category: 'series',
      arr: {
        ...base.arr,
        kind: 'sonarr',
        arrId: 55,
        state: 'downloaded',
        canDelete: true,
        addBlockedReason: 'alreadyInLibrary',
        externalUrl: 'http://sonarr/series/example',
        seasons: [{ number: 1, monitored: true, state: 'downloaded', episodeFileCount: 8, episodeCount: 8, totalEpisodeCount: 8 }],
      },
    })
    const user = userEvent.setup()
    renderDialog()

    const disclosure = await screen.findByText('Season details')
    expect(disclosure.parentElement).not.toHaveAttribute('open')
    expect(screen.getByRole('link', { name: 'Open in Sonarr' })).toHaveAttribute('target', '_blank')
    await user.click(disclosure)
    expect(disclosure.parentElement).toHaveAttribute('open')
    await user.click(screen.getByRole('button', { name: 'Stop monitoring 1' }))
    await waitFor(() => expect(setTitleSeasonMonitoring).toHaveBeenCalledWith(42, 1, false))
  })

  it('renders ordered cast members with placeholders and conditional IMDb links', async () => {
    vi.mocked(fetchTitle).mockResolvedValueOnce({
      id: 42,
      name: 'Dialog Movie',
      originalTitle: 'Dialog Movie',
      year: 2021,
      category: 'movie',
      overview: null,
      runtimeMinutes: null,
      seasonCount: null,
      episodeCount: null,
      genres: [],
      posterUrl: null,
      backdropUrl: null,
      ratings: { tmdb: null, tmdbVotes: null },
      links: { tmdb: null, imdb: 'https://www.imdb.com/title/tt1234567/', tvdb: null },
      isFavorite: false,
      isWatched: false,
      rating: null,
      metadataStatus: 'ready',
      availability: [],
      cast: ['Actor One'],
      castMembers: [
        {
          name: 'Actor One',
          photoUrl: '/actor-one.jpg',
          imdbUrl: 'https://www.imdb.com/name/nm0000001/',
        },
        { name: 'Actor Two', photoUrl: null, imdbUrl: null },
        { name: 'Actor One', photoUrl: null, imdbUrl: null },
      ],
      arr: {
        kind: 'radarr',
        arrId: null,
        state: 'notInLibrary',
        canAdd: false,
        canDelete: false,
        addBlockedReason: 'notConfigured',
        monitored: null,
        progress: null,
        sizeOnDisk: null,
        path: null,
        fileCount: null,
        syncedAt: null,
        seasons: null,
        externalUrl: null,
      },
    } as TitleDetail)

    renderDialog()

    const cast = await screen.findByRole('list', { name: 'Cast' })
    expect(cast).toHaveClass('overflow-x-auto', 'max-w-full', 'pb-4')
    expect(screen.getByRole('link', { name: 'Cast' })).toHaveAttribute('href', 'https://www.imdb.com/title/tt1234567/fullcredits/')
    expect(screen.getByRole('link', { name: 'Cast' })).toHaveAttribute('target', '_blank')
    expect(cast.querySelectorAll('[role="listitem"]')).toHaveLength(2)
    expect(screen.getByAltText('Actor One')).toBeTruthy()
    expect(screen.getByText('Actor Two')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Open IMDb page for Actor One' })).toHaveAttribute(
      'href',
      'https://www.imdb.com/name/nm0000001/',
    )
    expect(screen.queryByRole('link', { name: 'Open IMDb page for Actor Two' })).toBeNull()
  })

  it('lets the user like a title from the detail dialog', async () => {
    const user = userEvent.setup()
    renderDialog()

    expect(await screen.findByRole('heading', { name: 'Dialog Movie' })).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Like' }))

    await waitFor(() => expect(setRating).toHaveBeenCalledWith(42, 'like'))
  })

  it('lets the user mark a title as watched from the detail dialog', async () => {
    const user = userEvent.setup()
    renderDialog()

    expect(await screen.findByRole('button', { name: 'Hide' })).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Hide' }))

    await waitFor(() => expect(addWatched).toHaveBeenCalledWith(42))
  })
})
