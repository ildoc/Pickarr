import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { RatingButtons } from './RatingButtons'

vi.mock('../../api/ratings', () => ({
  setRating: vi.fn(async () => undefined),
  clearRating: vi.fn(async () => undefined),
}))

vi.mock('../../api/settings', () => ({
  fetchSettings: vi.fn(async () => ({
    reco: { enabled: true },
  })),
}))

import { clearRating, setRating } from '../../api/ratings'
import { fetchSettings } from '../../api/settings'

function renderButtons(rating: 'love' | 'like' | 'dislike' | 'not_interested' | null) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <I18nextProvider i18n={i18n}>
        <RatingButtons titleId={7} rating={rating} />
      </I18nextProvider>
    </QueryClientProvider>,
  )
}

describe('RatingButtons', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(setRating).mockResolvedValue(undefined)
    vi.mocked(clearRating).mockResolvedValue(undefined)
    vi.mocked(fetchSettings).mockResolvedValue({
      reco: { enabled: true },
    } as Awaited<ReturnType<typeof fetchSettings>>)
  })

  it('sets love when inactive', async () => {
    const user = userEvent.setup()
    renderButtons(null)
    await user.click(await screen.findByRole('button', { name: 'Love' }))
    expect(setRating).toHaveBeenCalledWith(7, 'love')
  })

  it('sets like when inactive', async () => {
    const user = userEvent.setup()
    renderButtons(null)
    await user.click(await screen.findByRole('button', { name: 'Like' }))
    expect(setRating).toHaveBeenCalledWith(7, 'like')
  })

  it('clears like on second click', async () => {
    const user = userEvent.setup()
    renderButtons('like')
    await user.click(await screen.findByRole('button', { name: 'Like' }))
    expect(clearRating).toHaveBeenCalledWith(7)
  })

  it('switches from like to dislike', async () => {
    const user = userEvent.setup()
    renderButtons('like')
    await user.click(await screen.findByRole('button', { name: 'Dislike' }))
    expect(setRating).toHaveBeenCalledWith(7, 'dislike')
  })

  it('sets not interested', async () => {
    const user = userEvent.setup()
    renderButtons(null)
    await user.click(await screen.findByRole('button', { name: 'Not interested' }))
    expect(setRating).toHaveBeenCalledWith(7, 'not_interested')
  })

  it('fills the circular button when a rating is active', async () => {
    renderButtons('not_interested')
    const button = await screen.findByRole('button', { name: 'Not interested' })
    expect(button).toHaveAttribute('aria-pressed', 'true')
    expect(button.className).toContain('bg-slate-500')
    expect(button.className).toContain('text-white')
  })

  it('hides when recommendations are disabled', async () => {
    vi.mocked(fetchSettings).mockResolvedValue({
      reco: { enabled: false },
    } as Awaited<ReturnType<typeof fetchSettings>>)
    renderButtons(null)
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Like' })).toBeNull()
    })
  })

  it('shows the selected vote and prevents accidental double clicks while saving', async () => {
    let finish!: () => void
    vi.mocked(setRating).mockReturnValue(new Promise<void>((resolve) => { finish = resolve }))
    const user = userEvent.setup()
    renderButtons(null)
    const like = await screen.findByRole('button', { name: 'Like' })
    await user.click(like)
    await waitFor(() => expect(like).toHaveAttribute('aria-pressed', 'true'))
    expect(like).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Dislike' })).toBeDisabled()
    await user.click(like)
    expect(setRating).toHaveBeenCalledTimes(1)
    await act(async () => finish())
    await waitFor(() => expect(like).toBeEnabled())
  })

  it('reports a failed save, rolls back the visual vote and lets the user retry', async () => {
    vi.mocked(setRating).mockRejectedValueOnce(new Error('Write failed'))
    const user = userEvent.setup()
    renderButtons(null)
    const like = await screen.findByRole('button', { name: 'Like' })
    await user.click(like)
    expect(await screen.findByRole('alert')).toHaveTextContent('Rating was not saved')
    expect(like).toHaveAttribute('aria-pressed', 'false')
    expect(like).toBeEnabled()
    await user.click(like)
    await waitFor(() => expect(screen.queryByRole('alert')).toBeNull())
    expect(setRating).toHaveBeenCalledTimes(2)
  })
})
