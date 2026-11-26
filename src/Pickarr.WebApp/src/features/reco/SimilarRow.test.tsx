import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchSimilarTitles } from '../../api/recommendations'
import { fetchSettings } from '../../api/settings'
import i18n from '../../i18n'
import { SimilarRow } from './SimilarRow'

vi.mock('../../api/recommendations', () => ({
  fetchSimilarTitles: vi.fn(),
}))

vi.mock('../../api/settings', () => ({
  fetchSettings: vi.fn(),
}))

function LocationProbe() {
  const location = useLocation()
  return <div data-testid="loc">{location.pathname + location.search}</div>
}

function wrap(
  ui: ReactNode,
  path = '/catalog?title=10&source=1',
  seedSettings?: Awaited<ReturnType<typeof fetchSettings>>,
) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  if (seedSettings) {
    client.setQueryData(['settings'], seedSettings)
  }
  return render(
    <QueryClientProvider client={client}>
      <I18nextProvider i18n={i18n}>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route
              path="/catalog"
              element={
                <>
                  {ui}
                  <LocationProbe />
                </>
              }
            />
          </Routes>
        </MemoryRouter>
      </I18nextProvider>
    </QueryClientProvider>,
  )
}

describe('SimilarRow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(fetchSettings).mockResolvedValue({
      reco: { enabled: true },
    } as Awaited<ReturnType<typeof fetchSettings>>)
  })

  it('loads similar titles and opens one on the same route', async () => {
    const user = userEvent.setup()
    vi.mocked(fetchSimilarTitles).mockResolvedValue({
      items: [
        {
          id: 99,
          name: 'Sibling Title',
          originalTitle: null,
          year: 2020,
          category: 'movie',
          posterUrl: null,
          genres: [],
          ratingTmdb: null,
          ratingScore: null,
          isFavorite: false,
          availability: null,
          arr: null,
        },
      ],
    })

    wrap(<SimilarRow titleId={10} sourceId={1} />)

    const show = await screen.findByRole('button', { name: 'Show similar titles' })
    expect(fetchSimilarTitles).not.toHaveBeenCalled()
    await user.click(show)
    expect(await screen.findByText('Similar titles')).toBeTruthy()
    await waitFor(() => expect(fetchSimilarTitles).toHaveBeenCalledWith(10, 1))
    await user.click(screen.getByRole('button', { name: /Sibling Title/i }))
    expect(screen.getByTestId('loc').textContent).toBe('/catalog?title=99&source=1')
  })

  it('shows progress, offers a retry on failure, and reports empty results', async () => {
    const user = userEvent.setup()
    let reject!: (reason: Error) => void
    vi.mocked(fetchSimilarTitles)
      .mockImplementationOnce(() => new Promise((_, fail) => { reject = fail }))
      .mockResolvedValueOnce({ items: [] })
    wrap(<SimilarRow titleId={10} sourceId={1} />)

    await user.click(await screen.findByRole('button', { name: 'Show similar titles' }))
    expect(await screen.findByRole('status')).toHaveTextContent('Loading similar titles')
    reject(new Error('unavailable'))
    expect(await screen.findByRole('alert')).toHaveTextContent("Couldn't load similar titles")
    await user.click(screen.getByRole('button', { name: 'Show similar titles' }))
    expect(await screen.findByText('No similar titles found.')).toBeTruthy()
    expect(fetchSimilarTitles).toHaveBeenCalledTimes(2)
  })

  it('renders nothing when recommendations are disabled', async () => {
    const disabled = {
      reco: { enabled: false },
    } as Awaited<ReturnType<typeof fetchSettings>>
    vi.mocked(fetchSettings).mockResolvedValue(disabled)

    wrap(<SimilarRow titleId={10} sourceId={1} />, '/catalog?title=10&source=1', disabled)

    expect(screen.queryByRole('button', { name: 'Show similar titles' })).toBeNull()
    expect(fetchSimilarTitles).not.toHaveBeenCalled()
  })
})
