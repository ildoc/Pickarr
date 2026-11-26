import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { I18nextProvider } from 'react-i18next'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchCatalogSetup, type CatalogSetup } from '../../api/catalogSetup'
import i18n from '../../i18n'
import { CatalogSourcesPage } from './CatalogSourcesPage'

vi.mock('../../api/catalogSetup', () => ({
  fetchCatalogSetup: vi.fn(),
  updateCatalogSetup: vi.fn(),
}))

function wrap(ui: ReactNode) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <I18nextProvider i18n={i18n}>{ui}</I18nextProvider>
    </QueryClientProvider>,
  )
}

describe('CatalogSourcesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows a load error when the catalog list cannot be fetched', async () => {
    vi.mocked(fetchCatalogSetup).mockRejectedValue(new Error('network'))
    wrap(<CatalogSourcesPage />)
    expect(await screen.findByText('Could not load the catalog selection.')).toBeTruthy()
  })

  it('fills the meter with the monthly estimate against the plan quota', async () => {
    vi.mocked(fetchCatalogSetup).mockResolvedValue(catalogSetup())
    wrap(<CatalogSourcesPage />)

    const meter = await screen.findByRole('meter')
    expect(meter.getAttribute('aria-valuemax')).toBe('2500')
    expect(meter.getAttribute('aria-valuetext')).toBe('248 of 2500')
    expect(meter.firstElementChild).toHaveStyle({ width: '10%' })
    expect(meter.firstElementChild?.className).toContain('bg-emerald-400')
    expect(screen.getByText('918 already used. 959 of 2500 by the reset, including the one-time fill.')).toBeTruthy()
  })

  it('leaves a country off the selection when Watchmode does not enable it', async () => {
    vi.mocked(fetchCatalogSetup).mockResolvedValue(
      catalogSetup({
        countries: [
          country('US', true),
          country('GB', true, false),
        ],
      }),
    )
    wrap(<CatalogSourcesPage />)

    const blocked = await screen.findByRole('checkbox', { name: /United Kingdom/ })
    expect(blocked).toBeDisabled()
    expect(blocked).not.toBeChecked()
    expect(screen.getByText('Not enabled for this Watchmode account, so Pickarr will not sync it.')).toBeTruthy()
    expect(screen.getByRole('checkbox', { name: /United States/ })).toBeChecked()
  })

  it('fills the meter completely when a full month passes the quota', async () => {
    vi.mocked(fetchCatalogSetup).mockResolvedValue(
      catalogSetup({
        pages: 600,
        used: 0,
      }),
    )
    wrap(<CatalogSourcesPage />)

    const meter = await screen.findByRole('meter')
    expect(meter.getAttribute('aria-valuetext')).toBe('2520 of 2500')
    expect(meter.firstElementChild).toHaveStyle({ width: '100%' })
    expect(meter.firstElementChild?.className).toContain('bg-red-400')
  })
})

function country(
  code: string,
  enabled: boolean,
  watchmodeEnabled = true,
  pages = 32,
): CatalogSetup['providers'][number]['countries'][number] {
  return {
    code,
    enabled,
    pages,
    pagesMeasured: true,
    incrementalPages: 3,
    fullyImported: true,
    watchmodeEnabled,
  }
}

function catalogSetup(
  overrides: {
    pages?: number
    used?: number
    countries?: CatalogSetup['providers'][number]['countries']
  } = {},
): CatalogSetup {
  const pages = overrides.pages ?? 32
  return {
    quota: 2500,
    used: overrides.used ?? 918,
    daysLeft: 5,
    daysInPeriod: 30,
    plan: 'free',
    freeCountryLimit: 3,
    creditsPerCall: 1,
    fullSyncsPerMonth: 4,
    dailyRunsPerMonth: 30,
    defaultPages: 32,
    providers: [
      {
        id: 1,
        key: 'netflix',
        name: 'Netflix',
        enabled: true,
        countries: overrides.countries ?? [country('US', true, true, pages)],
      },
    ],
  }
}
