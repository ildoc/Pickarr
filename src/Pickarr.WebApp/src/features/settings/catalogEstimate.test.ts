import { describe, expect, it } from 'vitest'
import { estimateCatalog } from './catalogEstimate'

const base = {
  used: 0,
  quota: 2500,
  freePlan: true,
  freeCountryLimit: 3,
  creditsPerCall: 1,
  fullSyncsPerMonth: 4,
  dailyRunsPerMonth: 30,
  daysLeft: 30,
  daysInPeriod: 30,
}

describe('estimateCatalog', () => {
  it('counts a fill and a month for each new country', () => {
    const result = estimateCatalog({
      ...base,
      countries: [{ code: 'US', selected: true, pages: 32, incrementalPages: 3, fullyImported: false }],
    })

    expect(result.countries).toBe(1)
    expect(result.catalogs).toBe(1)
    expect(result.fill).toBe(32)
    expect(result.monthly).toBe(3 * 30 + 32 * 4 + 30)
    expect(result.remaining).toBe(result.monthly)
    expect(result.projected).toBe(result.monthly + result.fill)
    expect(result.overQuota).toBe(false)
    expect(result.overCountryLimit).toBe(false)
  })

  it('ignores countries that are not selected and flags a fourth country', () => {
    const country = (code: string, selected = true) => ({
      code,
      selected,
      pages: 32,
      incrementalPages: 3,
      fullyImported: true,
    })
    const result = estimateCatalog({
      ...base,
      countries: [country('US'), country('IT'), country('GB'), country('DE'), country('FR', false)],
    })

    expect(result.countries).toBe(4)
    expect(result.catalogs).toBe(4)
    expect(result.fill).toBe(0)
    expect(result.overCountryLimit).toBe(true)
  })

  it('counts several providers in one country as a single country', () => {
    const catalog = { code: 'US', selected: true, pages: 32, incrementalPages: 3, fullyImported: false }
    const result = estimateCatalog({
      ...base,
      countries: [catalog, catalog, catalog, catalog],
    })

    expect(result.countries).toBe(1)
    expect(result.catalogs).toBe(4)
    expect(result.fill).toBe(128)
    expect(result.overCountryLimit).toBe(false)
  })

  it('flags a selection that would pass the monthly quota on the free plan', () => {
    const result = estimateCatalog({
      ...base,
      used: 2400,
      countries: [{ code: 'US', selected: true, pages: 400, incrementalPages: 3, fullyImported: false }],
    })

    expect(result.overQuota).toBe(true)
  })

  it('does not flag over-quota on the paid plan', () => {
    const result = estimateCatalog({
      ...base,
      freePlan: false,
      used: 2400,
      countries: [{ code: 'US', selected: true, pages: 400, incrementalPages: 3, fullyImported: false }],
    })

    expect(result.overQuota).toBe(false)
  })

  it('scales the monthly updates to the days left instead of adding a whole month to credits already used', () => {
    const result = estimateCatalog({
      ...base,
      used: 918,
      daysLeft: 5,
      daysInPeriod: 30,
      countries: [{ code: 'US', selected: true, pages: 32, incrementalPages: 3, fullyImported: true }],
    })

    expect(result.monthly).toBe(248)
    expect(result.remaining).toBe(41)
    expect(result.projected).toBe(959)
    expect(result.overPeriod).toBe(false)
    expect(result.overMonth).toBe(false)
    expect(result.overQuota).toBe(false)
  })

  it('still flags a full month that does not fit even when little of the period is left', () => {
    const result = estimateCatalog({
      ...base,
      used: 0,
      daysLeft: 1,
      daysInPeriod: 30,
      countries: [{ code: 'US', selected: true, pages: 600, incrementalPages: 3, fullyImported: true }],
    })

    expect(result.monthly).toBe(2520)
    expect(result.remaining).toBe(84)
    expect(result.projected).toBe(84)
    expect(result.overPeriod).toBe(false)
    expect(result.overMonth).toBe(true)
    expect(result.overQuota).toBe(true)
  })

  it('counts the one-time fill in full against the days left', () => {
    const result = estimateCatalog({
      ...base,
      used: 2300,
      daysLeft: 5,
      daysInPeriod: 30,
      countries: [{ code: 'US', selected: true, pages: 200, incrementalPages: 3, fullyImported: false }],
    })

    expect(result.fill).toBe(200)
    expect(result.overPeriod).toBe(true)
    expect(result.overQuota).toBe(true)
  })
})
