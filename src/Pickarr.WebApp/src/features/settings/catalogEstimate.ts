export type EstimateCountry = {
  code: string
  selected: boolean
  pages: number
  incrementalPages: number
  fullyImported: boolean
}

export type EstimateInput = {
  countries: EstimateCountry[]
  used: number
  quota: number
  freePlan: boolean
  freeCountryLimit: number
  creditsPerCall: number
  fullSyncsPerMonth: number
  dailyRunsPerMonth: number
  daysLeft: number
  daysInPeriod: number
}

export type CatalogEstimate = {
  countries: number
  catalogs: number
  fill: number
  monthly: number
  remaining: number
  projected: number
  overPeriod: boolean
  overMonth: boolean
  overQuota: boolean
  overCountryLimit: boolean
}

/** Share of a full-period figure still left. Rounds half away from zero, matching the API. */
export function scaleToRemainingDays(amount: number, daysLeft: number, daysInPeriod: number): number {
  if (amount <= 0 || daysLeft <= 0 || daysInPeriod <= 0) return 0
  if (daysLeft >= daysInPeriod) return amount
  return Math.round(amount * (daysLeft / daysInPeriod))
}

export function estimateCatalog(input: EstimateInput): CatalogEstimate {
  const selected = input.countries.filter((country) => country.selected)
  const countries = new Set(selected.map((country) => country.code.trim().toUpperCase())).size
  const fill = selected
    .filter((country) => !country.fullyImported)
    .reduce((sum, country) => sum + country.pages * input.creditsPerCall, 0)
  const monthly = selected.reduce(
    (sum, country) =>
      sum +
      (country.incrementalPages * input.dailyRunsPerMonth +
        country.pages * input.fullSyncsPerMonth +
        input.dailyRunsPerMonth) *
        input.creditsPerCall,
    0,
  )
  const remaining = scaleToRemainingDays(monthly, input.daysLeft, input.daysInPeriod)
  const projected = input.used + remaining + fill
  const overPeriod = input.freePlan && projected > input.quota
  const overMonth = input.freePlan && monthly > input.quota

  return {
    countries,
    catalogs: selected.length,
    fill,
    monthly,
    remaining,
    projected,
    overPeriod,
    overMonth,
    overQuota: overPeriod || overMonth,
    overCountryLimit: input.freePlan && countries > input.freeCountryLimit,
  }
}
