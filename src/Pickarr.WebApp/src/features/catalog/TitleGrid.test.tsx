import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { TitleListItem } from '../../api/types'
import i18n from '../../i18n'
import { TitleGrid } from './TitleGrid'

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

function card(id: number, name: string): TitleListItem {
  return {
    id,
    name,
    originalTitle: name,
    year: 2020,
    category: 'movie',
    posterUrl: null,
    genres: [],
    ratingTmdb: null,
    ratingScore: null,
    isFavorite: false,
    availability: null,
    arr: null,
  }
}

function renderGrid(
  items: TitleListItem[],
  options: { hasMore?: boolean; onLoadMore?: () => void } = {},
) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const ui: ReactNode = (
    <QueryClientProvider client={client}>
      <I18nextProvider i18n={i18n}>
        <MemoryRouter>
          <TitleGrid
            items={items}
            hasMore={options.hasMore ?? false}
            isFetchingNextPage={false}
            onLoadMore={options.onLoadMore ?? (() => undefined)}
          />
        </MemoryRouter>
      </I18nextProvider>
    </QueryClientProvider>
  )
  return render(ui)
}

describe('TitleGrid', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('requests the next page when the list end is on screen', () => {
    const onLoadMore = vi.fn()
    class FakeObserver {
      private readonly callback: IntersectionObserverCallback
      constructor(callback: IntersectionObserverCallback) {
        this.callback = callback
      }
      observe() {
        this.callback(
          [{ isIntersecting: true } as IntersectionObserverEntry],
          this as unknown as IntersectionObserver,
        )
      }
      disconnect() {}
      unobserve() {}
    }
    vi.stubGlobal('IntersectionObserver', FakeObserver)
    renderGrid([card(1, 'Only title')], { hasMore: true, onLoadMore })
    expect(onLoadMore).toHaveBeenCalledOnce()
    expect(screen.queryByRole('button', { name: 'Load more' })).toBeNull()
  })

  it('spreads rows when a card row is taller than the estimate', () => {
    class FakeResize {
      private readonly callback: ResizeObserverCallback
      constructor(callback: ResizeObserverCallback) {
        this.callback = callback
      }
      observe() {
        this.callback(
          [{ contentRect: { height: 480 } } as ResizeObserverEntry],
          this as unknown as ResizeObserver,
        )
      }
      disconnect() {}
      unobserve() {}
    }
    vi.stubGlobal('ResizeObserver', FakeResize)
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 })
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 800 })
    Object.defineProperty(window, 'scrollY', { configurable: true, value: 0 })

    const items = Array.from({ length: 10 }, (_, index) => card(index + 1, `Card ${index + 1}`))
    const { container } = renderGrid(items)
    const list = container.querySelector('.relative') as HTMLElement
    expect(list.style.height).toBe('976px')
  })

  it('renders a single copy when a later page repeats an id', () => {
    renderGrid([card(1, 'First copy'), card(1, 'Second copy')])
    expect(screen.getAllByRole('link', { name: /First copy/ })).toHaveLength(1)
    expect(screen.queryByRole('link', { name: /Second copy/ })).toBeNull()
  })

  it('mounts only the rows near the viewport', () => {
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 400 })
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 })
    Object.defineProperty(window, 'scrollY', { configurable: true, value: 0 })
    const items = Array.from({ length: 80 }, (_, index) => card(index + 1, `Card ${index + 1}`))
    items[0] = card(1, 'Head title')
    items[79] = card(80, 'Tail title')
    renderGrid(items)
    expect(screen.getByRole('link', { name: /Head title/ })).toBeTruthy()
    expect(screen.queryByRole('link', { name: /Tail title/ })).toBeNull()
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 768 })
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 })
  })

  it('still shows cards after a long scroll', () => {
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 800 })
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 })
    const items = Array.from({ length: 80 }, (_, index) => card(index + 1, `Card ${index + 1}`))
    renderGrid(items)
    for (let y = 0; y <= 4000; y += 200) {
      Object.defineProperty(window, 'scrollY', { configurable: true, value: y })
      act(() => {
        window.dispatchEvent(new Event('scroll'))
      })
    }
    expect(screen.getAllByRole('link').length).toBeGreaterThan(0)
    Object.defineProperty(window, 'scrollY', { configurable: true, value: 0 })
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 768 })
  })
})
