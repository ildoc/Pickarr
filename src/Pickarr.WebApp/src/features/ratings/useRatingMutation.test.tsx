import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query'
import { act, renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PagedTitles, RecommendationsPage, TitleListItem } from '../../api/types'
import { clearRating, setRating } from '../../api/ratings'
import { RECO_DISMISS_TOTAL_MS } from './ratingCache'
import { useRatingMutation } from './useRatingMutation'

vi.mock('../../api/ratings', () => ({
  setRating: vi.fn(async () => undefined),
  clearRating: vi.fn(async () => undefined),
}))

function listItem(id: number): TitleListItem {
  return {
    id,
    name: `Title ${id}`,
    originalTitle: null,
    year: 2020,
    category: 'movie',
    posterUrl: null,
    genres: [],
    ratingTmdb: null,
    ratingScore: null,
    isFavorite: false,
    rating: null,
    availability: null,
    arr: null,
    places: [],
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: Error) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

describe('useRatingMutation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(setRating).mockResolvedValue(undefined)
    vi.mocked(clearRating).mockResolvedValue(undefined)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('persists like even when my-list types/genres queries are cached', async () => {
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    })
    client.setQueryData(['my-list', 'types'], [{ type: 'movie', count: 1 }])
    client.setQueryData(['my-list', 'genres'], [{ id: 1, name: 'Drama', count: 1 }])
    client.setQueryData(
      ['titles', 1],
      {
        pages: [{ page: 1, pageSize: 48, total: 1, items: [listItem(9)] }],
        pageParams: [1],
      } satisfies { pages: PagedTitles[]; pageParams: number[] },
    )

    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    )

    const { result } = renderHook(() => useRatingMutation(9), { wrapper })
    result.current.mutate('like')

    await waitFor(() => expect(setRating).toHaveBeenCalledWith(9, 'like'))
    expect(clearRating).not.toHaveBeenCalled()

    const titles = client.getQueryData<{ pages: PagedTitles[] }>(['titles', 1])
    expect(titles?.pages[0]?.items[0]?.rating).toBe('like')
  })

  it('keeps a For you card until the dismiss window elapses', async () => {
    vi.useFakeTimers()
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    })
    client.setQueryData(
      ['recommendations', 'global'],
      {
        version: 1,
        stage: 'cold',
        page: 1,
        pageSize: 48,
        total: 1,
        items: [listItem(9)],
      } satisfies RecommendationsPage,
    )

    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    )

    const { result } = renderHook(() => useRatingMutation(9), { wrapper })
    act(() => {
      result.current.mutate('like')
    })

    await act(async () => {
      await Promise.resolve()
    })

    expect(client.getQueryData<RecommendationsPage>(['recommendations', 'global'])?.items).toHaveLength(
      1,
    )
    expect(
      client.getQueryData<RecommendationsPage>(['recommendations', 'global'])?.items[0]?.rating,
    ).toBe('like')

    act(() => {
      vi.advanceTimersByTime(RECO_DISMISS_TOTAL_MS)
    })

    expect(
      client.getQueryData<RecommendationsPage>(['recommendations', 'global'])?.items.map((i) => i.id),
    ).toEqual([])
  })

  it('allows only one pending write per title across separate hook instances', async () => {
    const write = deferred<void>()
    vi.mocked(setRating).mockReturnValue(write.promise)
    const client = new QueryClient()
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    )
    const { result } = renderHook(() => ({
      card: useRatingMutation(9),
      dialog: useRatingMutation(9),
    }), { wrapper })

    act(() => {
      result.current.card.mutate('like')
      result.current.dialog.mutate('dislike')
    })
    await waitFor(() => expect(setRating).toHaveBeenCalledTimes(1))
    expect(setRating).toHaveBeenCalledWith(9, 'like')
    await waitFor(() => expect(result.current.dialog.pendingRating).toBe('like'))
    expect(result.current.dialog.isPending).toBe(true)
    await act(async () => write.resolve())
    await waitFor(() => expect(result.current.dialog.isPending).toBe(false))
  })

  it('shares save errors with controls in another view of the same title', async () => {
    vi.mocked(setRating).mockRejectedValueOnce(new Error('Write failed'))
    const client = new QueryClient()
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    )
    const { result } = renderHook(() => ({
      keyboard: useRatingMutation(9), buttons: useRatingMutation(9),
    }), { wrapper })
    act(() => result.current.keyboard.mutate('like'))
    await waitFor(() => expect(result.current.buttons.isError).toBe(true))
    act(() => result.current.buttons.mutate('like'))
    await waitFor(() => expect(setRating).toHaveBeenCalledTimes(2))
    await waitFor(() => expect(result.current.keyboard.isError).toBe(false))
  })

  it('rolls back only the failed title and waits for all votes before refetching shared lists', async () => {
    const first = deferred<void>()
    const second = deferred<void>()
    vi.mocked(setRating).mockImplementation((id) => id === 9 ? first.promise : second.promise)
    const client = new QueryClient()
    const key = ['titles', 1]
    client.setQueryData(key, {
      pages: [{ page: 1, pageSize: 48, total: 2, items: [listItem(9), listItem(10)] }],
      pageParams: [1],
    })
    const invalidate = vi.spyOn(client, 'invalidateQueries')
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    )
    const { result } = renderHook(() => ({
      first: useRatingMutation(9), second: useRatingMutation(10),
    }), { wrapper })
    act(() => result.current.first.mutate('like'))
    await waitFor(() => expect(setRating).toHaveBeenCalledWith(9, 'like'))
    act(() => result.current.second.mutate('love'))
    await waitFor(() => expect(setRating).toHaveBeenCalledWith(10, 'love'))
    // A separate favorite update must also survive the rating rollback.
    client.setQueryData<{ pages: PagedTitles[] }>(key, (data) => data && ({
      ...data, pages: data.pages.map((page) => ({
        ...page, items: page.items.map((item) => ({ ...item, isFavorite: true })),
      })),
    }))
    await act(async () => first.reject(new Error('Write failed')))
    await waitFor(() => expect(result.current.first.isError).toBe(true))
    const page = client.getQueryData<{ pages: PagedTitles[] }>(key)?.pages[0]
    expect(page?.items.map((item) => item.rating)).toEqual([null, 'love'])
    expect(page?.items.every((item) => item.isFavorite)).toBe(true)
    expect(invalidate).not.toHaveBeenCalled()

    await act(async () => second.resolve())
    await waitFor(() => expect(result.current.second.isPending).toBe(false))
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['titles'] })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['title'] })
  })

  it('ignores an old in-flight list response instead of undoing the selected vote', async () => {
    const read = deferred<PagedTitles>()
    const write = deferred<void>()
    vi.mocked(setRating).mockReturnValue(write.promise)
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const key = ['titles', 1]
    const oldPage: PagedTitles = { page: 1, pageSize: 48, total: 1, items: [listItem(9)] }
    const oldData = { pages: [oldPage], pageParams: [1] }
    client.setQueryData(key, oldData)
    const fetchPage = vi.fn()
      .mockImplementationOnce(async () => ({ pages: [await read.promise], pageParams: [1] }))
      .mockResolvedValue({ pages: [{ ...oldPage, items: [{ ...listItem(9), rating: 'like' }] }], pageParams: [1] })
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    )
    const { result } = renderHook(() => ({
      page: useQuery({ queryKey: key, queryFn: fetchPage }),
      vote: useRatingMutation(9),
    }), { wrapper })
    await waitFor(() => expect(fetchPage).toHaveBeenCalledTimes(1))
    act(() => result.current.vote.mutate('like'))
    await waitFor(() => expect(setRating).toHaveBeenCalledWith(9, 'like'))
    await act(async () => read.resolve(oldPage))
    expect(client.getQueryData<typeof oldData>(key)?.pages[0].items[0].rating).toBe('like')
    await act(async () => write.resolve())
    await waitFor(() => expect(result.current.vote.isPending).toBe(false))
    expect(client.getQueryData<typeof oldData>(key)?.pages[0].items[0].rating).toBe('like')
  })

  it('refetches once when two different votes finish in the same event-loop turn', async () => {
    const first = deferred<void>()
    const second = deferred<void>()
    vi.mocked(setRating).mockImplementation((id) => id === 9 ? first.promise : second.promise)
    const client = new QueryClient()
    const invalidate = vi.spyOn(client, 'invalidateQueries')
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    )
    const { result } = renderHook(() => ({
      first: useRatingMutation(9), second: useRatingMutation(10),
    }), { wrapper })
    act(() => {
      result.current.first.mutate('like')
      result.current.second.mutate('love')
    })
    await waitFor(() => expect(setRating).toHaveBeenCalledTimes(2))
    await act(async () => {
      first.resolve()
      second.resolve()
    })
    await waitFor(() => {
      expect(invalidate.mock.calls.filter(([filters]) => filters?.queryKey?.[0] === 'titles')).toHaveLength(1)
    })
  })
})
