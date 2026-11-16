import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { SeasonTable } from './SeasonTable'

describe('SeasonTable', () => {
  it('allows monitoring and unmonitoring individual seasons', async () => {
    const user = userEvent.setup()
    const onToggleMonitoring = vi.fn()
    render(
      <I18nextProvider i18n={i18n}>
        <SeasonTable
          seasons={[
            { number: 2, monitored: false, state: 'missing', episodeFileCount: 0, episodeCount: 8, totalEpisodeCount: 8 },
            { number: 1, monitored: true, state: 'downloaded', episodeFileCount: 8, episodeCount: 8, totalEpisodeCount: 8 },
          ]}
          onToggleMonitoring={onToggleMonitoring}
          pendingSeason={null}
        />
      </I18nextProvider>,
    )

    await user.click(screen.getByRole('button', { name: 'Monitor 2' }))
    await user.click(screen.getByRole('button', { name: 'Stop monitoring 1' }))

    expect(onToggleMonitoring).toHaveBeenNthCalledWith(1, 2, true)
    expect(onToggleMonitoring).toHaveBeenNthCalledWith(2, 1, false)
  })
})
