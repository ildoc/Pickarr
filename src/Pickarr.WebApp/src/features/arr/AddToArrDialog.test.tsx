import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { I18nextProvider } from 'react-i18next'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchArrOptions, postTitleArr } from '../../api/arr'
import { fetchSeriesSeasons } from '../../api/catalog'
import { ApiError } from '../../api/client'
import type { TitleDetail } from '../../api/types'
import i18n from '../../i18n'
import { AddToArrDialog } from './AddToArrDialog'

vi.mock('../../api/arr', () => ({
  fetchArrOptions: vi.fn(),
  postTitleArr: vi.fn(),
}))

vi.mock('../../api/catalog', () => ({
  fetchSeriesSeasons: vi.fn(),
}))

const title = {
  id: 11,
  name: 'Example Series',
  arr: {
    kind: 'sonarr',
    arrId: null,
    state: 'notInLibrary',
    monitored: null,
    progress: null,
    sizeOnDisk: null,
    path: null,
    fileCount: null,
    syncedAt: null,
    seasons: null,
    canAdd: true,
    canDelete: false,
    addBlockedReason: null,
    externalUrl: null,
  },
} as TitleDetail

function wrap(ui: ReactNode) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <I18nextProvider i18n={i18n}>{ui}</I18nextProvider>
    </QueryClientProvider>,
  )
}

describe('AddToArrDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(fetchArrOptions).mockResolvedValue({
      kind: 'sonarr',
      configured: true,
      reachable: true,
      qualityProfiles: [{ id: 1, name: 'Any' }],
      rootFolders: [{ path: '/tv', freeSpace: 10 }],
      tags: [],
      defaults: { qualityProfileId: 1, rootFolderPath: '/tv' },
      monitorOptions: ['all', 'none'],
      seriesTypes: ['standard'],
    })
    vi.mocked(fetchSeriesSeasons).mockResolvedValue([
      { number: 2, name: 'Season 2', episodeCount: 8, airDate: null, overview: null },
      { number: 1, name: 'Season 1', episodeCount: 8, airDate: null, overview: null },
    ])
    vi.mocked(postTitleArr).mockResolvedValue(title.arr)
  })

  it('sends only the selected seasons in the add payload', async () => {
    const user = userEvent.setup()
    wrap(<AddToArrDialog title={title} open onClose={vi.fn()} onAdded={vi.fn()} />)

    expect(await screen.findByText('Seasons to add')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Clear' }))
    await user.click(screen.getByLabelText(/Season 2/i))
    await user.click(screen.getByRole('button', { name: /^Add$/i }))

    await waitFor(() => {
      expect(postTitleArr).toHaveBeenCalledWith(
        11,
        expect.objectContaining({
          qualityProfileId: 1,
          rootFolderPath: '/tv',
          seasons: [2],
        }),
      )
    })
  })

  it('shows the API error when add fails', async () => {
    const user = userEvent.setup()
    vi.mocked(postTitleArr).mockRejectedValue(new ApiError('ARR_ID_UNMAPPED', 422))
    wrap(<AddToArrDialog title={title} open onClose={vi.fn()} onAdded={vi.fn()} />)

    expect(await screen.findByText('Seasons to add')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: /^Add$/i }))

    expect(await screen.findByText('ARR_ID_UNMAPPED')).toBeTruthy()
    expect(screen.queryByRole('status')).toBeNull()
    expect(screen.getByRole('button', { name: /^Add$/i })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Close' })).toBeEnabled()
  })

  it.each(['sonarr', 'radarr'] as const)('shows pending feedback until %s finishes adding', async (kind) => {
    const user = userEvent.setup()
    let finishAdd!: (arr: TitleDetail['arr']) => void
    vi.mocked(postTitleArr).mockReturnValue(new Promise((resolve) => {
      finishAdd = resolve
    }))
    const onClose = vi.fn()
    const onAdded = vi.fn()
    wrap(
      <AddToArrDialog
        title={{ ...title, arr: { ...title.arr, kind } }}
        open
        onClose={onClose}
        onAdded={onAdded}
      />,
    )

    await user.click(await screen.findByRole('button', { name: /^Add$/i }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      `Adding to ${kind === 'sonarr' ? 'Sonarr' : 'Radarr'}...`,
    )
    expect(screen.getByRole('button', { name: 'Adding...' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Close' })).toBeDisabled()
    expect(onClose).not.toHaveBeenCalled()
    expect(onAdded).not.toHaveBeenCalled()
    expect(postTitleArr).toHaveBeenCalledTimes(1)

    await act(async () => finishAdd(title.arr))

    await waitFor(() => expect(onClose).toHaveBeenCalledOnce())
    expect(onAdded).toHaveBeenCalledOnce()
    expect(screen.queryByRole('status')).toBeNull()
  })
})
