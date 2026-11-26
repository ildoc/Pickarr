import { useEffect, useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'

const STAND_UP_GENRE_ID = '-1'

/** Stand-up used to be a type; links with `type=standup` now open the Stand-up genre. */
export function migrateLegacyType(params: URLSearchParams): URLSearchParams | null {
  if (params.get('type') !== 'standup') return null
  const next = new URLSearchParams(params)
  next.delete('type')
  if (!next.get('genre')) next.set('genre', STAND_UP_GENRE_ID)
  return next
}

export function useFilterSearchParams() {
  const [raw, setParams] = useSearchParams()
  const migrated = useMemo(() => migrateLegacyType(raw), [raw])
  useEffect(() => {
    if (migrated) setParams(migrated, { replace: true })
  }, [migrated, setParams])
  return [migrated ?? raw, setParams] as const
}
