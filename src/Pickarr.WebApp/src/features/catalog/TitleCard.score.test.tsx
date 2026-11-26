import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import type { TitleListItem } from '../../api/types'
import i18n from '../../i18n'
import { TitleCard } from './TitleCard'

vi.mock('../../api/settings', () => ({
  fetchSettings: vi.fn(async () => ({ reco: { enabled: true } })),
}))

vi.mock('../../api/favorites', () => ({
  addFavorite: vi.fn(),
  removeFavorite: vi.fn(),
}))

vi.mock('../../api/ratings', () => ({
  setRating: vi.fn(),
  clearRating: vi.fn(),
}))

function item(ratingScore: number | null, ratingTmdb: number | null): TitleListItem {
  return {
    id: 7,
    name: 'Scored Movie',
    originalTitle: null,
    year: 2024,
    category: 'movie',
    posterUrl: null,
    genres: [],
    ratingTmdb,
    ratingScore,
    isFavorite: false,
    availability: null,
    arr: null,
  }
}

function renderCard(title: TitleListItem) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const ui: ReactNode = (
    <QueryClientProvider client={client}>
      <I18nextProvider i18n={i18n}>
        <MemoryRouter>
          <TitleCard title={title} />
        </MemoryRouter>
      </I18nextProvider>
    </QueryClientProvider>
  )
  return render(ui)
}

describe('TitleCard audience score', () => {
  it('shows the blended score to one decimal', () => {
    renderCard(item(7.44, 9.1))

    expect(screen.getByText('7.4')).toBeTruthy()
    expect(screen.queryByText('9.1')).toBeNull()
  })

  it('hides the badge when the blended score is missing', () => {
    renderCard(item(null, 9.1))

    expect(screen.queryByText('9.1')).toBeNull()
    expect(screen.queryByText('7.4')).toBeNull()
  })
})
