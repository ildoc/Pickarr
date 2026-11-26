import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchCatalogTypes, fetchGenres, fetchSources, fetchTitles } from '../../api/catalog'
import { fetchProfiles } from '../../api/profiles'
import { fetchRecommendations } from '../../api/recommendations'
import { fetchSettings } from '../../api/settings'
import i18n from '../../i18n'
import { ProfileProvider } from '../who/profileContext'
import { CatalogPage } from './CatalogPage'

vi.mock('../../api/catalog', () => ({
  fetchSources: vi.fn(),
  fetchTitles: vi.fn(),
  fetchCatalogTypes: vi.fn(),
  fetchGenres: vi.fn(),
  fetchTitle: vi.fn(),
}))

vi.mock('../../api/profiles', () => ({
  fetchProfiles: vi.fn(),
}))

vi.mock('../../api/recommendations', () => ({
  fetchRecommendations: vi.fn(),
}))

vi.mock('../../api/settings', () => ({
  fetchSettings: vi.fn(),
}))

vi.mock('../title/TitleDialog', () => ({
  TitleDialog: () => null,
}))

const sources = [
  {
    id: 1,
    providerKey: 'netflix',
    providerName: 'Netflix',
    countryCode: 'IT',
    metadataLanguage: 'it-IT',
    baselineCompleted: true,
    lastFullSyncAt: null,
    dataStale: false,
  },
  {
    id: 2,
    providerKey: 'netflix',
    providerName: 'Netflix',
    countryCode: 'US',
    metadataLanguage: 'en-US',
    baselineCompleted: true,
    lastFullSyncAt: null,
    dataStale: false,
  },
]

function renderAt(path: string) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  const ui: ReactNode = (
    <QueryClientProvider client={client}>
      <I18nextProvider i18n={i18n}>
        <ProfileProvider>
          <MemoryRouter initialEntries={[path]}>
            <Routes>
              <Route path="/catalog" element={<CatalogPage />} />
            </Routes>
          </MemoryRouter>
        </ProfileProvider>
      </I18nextProvider>
    </QueryClientProvider>
  )
  return render(ui)
}

describe('CatalogPage', () => {
  beforeEach(() => {
    const store = new Map<string, string>()
    vi.stubGlobal('localStorage', {
      getItem: (key: string) => store.get(key) ?? null,
      setItem: (key: string, value: string) => {
        store.set(key, value)
      },
      removeItem: (key: string) => {
        store.delete(key)
      },
      clear: () => {
        store.clear()
      },
    })
    vi.clearAllMocks()
    vi.mocked(fetchSources).mockResolvedValue(sources)
    vi.mocked(fetchProfiles).mockResolvedValue([
      {
        id: 1,
        name: 'Admin',
        color: 'blue',
        isAdmin: true,
        useImplicit: true,
        excludeInLibrary: true,
        createdAt: '2026-11-10T00:00:00Z',
      },
    ])
    vi.mocked(fetchCatalogTypes).mockResolvedValue([{ type: 'movie', count: 1 }])
    vi.mocked(fetchGenres).mockResolvedValue([])
    vi.mocked(fetchSettings).mockResolvedValue({
      reco: { enabled: true },
    } as Awaited<ReturnType<typeof fetchSettings>>)
    vi.mocked(fetchRecommendations).mockResolvedValue({
      version: 1,
      stage: 'cold',
      page: 1,
      pageSize: 25,
      total: 0,
      items: [],
    })
    vi.mocked(fetchTitles).mockResolvedValue({
      page: 1,
      pageSize: 48,
      total: 0,
      items: [],
    })
  })

  it('never sends favorite on catalog title queries', async () => {
    renderAt('/catalog?source=1')

    await waitFor(() => expect(fetchTitles).toHaveBeenCalled())
    const args = vi.mocked(fetchTitles).mock.calls.at(-1)
    expect(args?.[0]).toBe(1)
    expect(args?.[1]).not.toHaveProperty('favorite')
  })

  it('updates the titles request view when a quick filter is chosen', async () => {
    const user = userEvent.setup()
    renderAt('/catalog?source=1')

    await waitFor(() => expect(fetchTitles).toHaveBeenCalled())
    await user.click(screen.getByRole('button', { name: 'Leaving' }))

    await waitFor(() =>
      expect(fetchTitles).toHaveBeenCalledWith(
        1,
        expect.objectContaining({ view: 'leaving' }),
      ),
    )
  })

  it('filters downloaded titles server-side and clears the library filter on All', async () => {
    const user = userEvent.setup()
    renderAt('/catalog?source=1&q=Example&type=movie')

    await waitFor(() => expect(fetchTitles).toHaveBeenCalled())
    await user.click(screen.getByRole('button', { name: 'In library' }))

    await waitFor(() =>
      expect(fetchTitles).toHaveBeenLastCalledWith(
        1,
        expect.objectContaining({
          view: 'all', arrState: 'downloaded', q: 'Example', type: 'movie', page: 1,
        }),
      ),
    )
    expect(screen.getByRole('button', { name: 'In library' })).toHaveAttribute('aria-pressed', 'true')

    await user.click(screen.getByRole('button', { name: 'All' }))

    await waitFor(() =>
      expect(fetchTitles).toHaveBeenLastCalledWith(
        1,
        expect.objectContaining({ view: 'all', arrState: undefined, q: 'Example', type: 'movie' }),
      ),
    )
  })

  it('restores the library filter from the URL and clears only the text search', async () => {
    const user = userEvent.setup()
    renderAt('/catalog?source=1&view=library&q=Example&type=movie')

    await waitFor(() =>
      expect(fetchTitles).toHaveBeenCalledWith(
        1,
        expect.objectContaining({ arrState: 'downloaded', q: 'Example', type: 'movie' }),
      ),
    )
    await user.click(screen.getByRole('button', { name: 'Clear search' }))

    expect(screen.getByRole('searchbox')).toHaveValue('')
    expect(screen.queryByRole('button', { name: 'Clear search' })).toBeNull()
    await waitFor(() =>
      expect(fetchTitles).toHaveBeenLastCalledWith(
        1,
        expect.objectContaining({
          arrState: 'downloaded', q: undefined, type: 'movie', sort: 'added', page: 1,
        }),
      ),
    )
  })

  it('updates recommendations when type and genre filters change or are cleared', async () => {
    const user = userEvent.setup()
    vi.mocked(fetchGenres).mockResolvedValue([{ id: 28, name: 'Action', count: 1 }])
    vi.mocked(fetchRecommendations).mockImplementation(async ({ type, genre }) => {
      const name = type && genre ? 'Action movie' : type ? 'Any movie' : genre ? 'Any action' : 'Any title'
      return {
        version: 1,
        stage: 'cold',
        page: 1,
        pageSize: 25,
        total: 1,
        items: [{
          id: 11,
          name,
          originalTitle: null,
          year: 2024,
          category: 'movie',
          posterUrl: null,
          genres: [],
          ratingTmdb: null,
          ratingScore: null,
          isFavorite: false,
          rating: null,
          availability: null,
          arr: null,
        }],
      }
    })
    renderAt('/catalog?source=1')

    expect((await screen.findAllByText('Any title')).length).toBeGreaterThan(0)
    await user.selectOptions(screen.getByLabelText('Type'), 'movie')
    expect((await screen.findAllByText('Any movie')).length).toBeGreaterThan(0)
    await user.selectOptions(screen.getByLabelText('Genre'), '28')
    expect((await screen.findAllByText('Action movie')).length).toBeGreaterThan(0)
    expect(fetchRecommendations).toHaveBeenLastCalledWith(expect.objectContaining({
      scope: 'source', sourceId: 1, type: 'movie', genre: '28', page: 1, pageSize: 25,
    }))
    await user.selectOptions(screen.getByLabelText('Type'), '')
    expect((await screen.findAllByText('Any action')).length).toBeGreaterThan(0)
    await user.selectOptions(screen.getByLabelText('Genre'), '')
    expect((await screen.findAllByText('Any title')).length).toBeGreaterThan(0)
  })

  it('uses the preferred source from localStorage when the URL has none', async () => {
    localStorage.setItem('pickarr.sourceId', '2')
    renderAt('/catalog')

    await waitFor(() =>
      expect(fetchTitles).toHaveBeenCalledWith(2, expect.objectContaining({ page: 1 })),
    )
  })
})
