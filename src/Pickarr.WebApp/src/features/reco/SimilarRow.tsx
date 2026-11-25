import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useLocation, useSearchParams } from 'react-router-dom'
import { fetchSimilarTitles } from '../../api/recommendations'
import { fetchSettings } from '../../api/settings'

type SimilarProps = {
  titleId: number
  sourceId: number | null
}

export function SimilarRow(props: SimilarProps) {
  const settings = useQuery({ queryKey: ['settings'], queryFn: fetchSettings })
  if (!settings.data?.reco.enabled) return null
  return <SimilarRequest key={`${props.titleId}:${props.sourceId}`} {...props} />
}

function SimilarRequest({ titleId, sourceId }: SimilarProps) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const [params] = useSearchParams()
  const [requested, setRequested] = useState(false)

  const similar = useQuery({
    queryKey: ['similar', titleId, sourceId],
    queryFn: () => fetchSimilarTitles(titleId, sourceId),
    enabled: false,
    retry: false,
  })

  if (!requested || similar.isError) {
    return (
      <div className="space-y-2 border-t border-white/10 px-6 pb-6 pt-4">
        {requested && <p role="alert">{t('reco.similarError')}</p>}
        <button type="button" className="rounded-md border border-white/20 px-3 py-2 text-sm"
          onClick={() => {
            setRequested(true)
            void similar.refetch()
          }}>
          {t('reco.showSimilar')}
        </button>
      </div>
    )
  }
  if (similar.isFetching || !similar.data) {
    return (
      <p role="status" className="flex items-center gap-2 border-t border-white/10 px-6 pb-6 pt-4 text-sm text-[var(--muted)]">
        <span aria-hidden="true" className="h-4 w-4 rounded-full border-2 border-current border-t-transparent motion-safe:animate-spin" />
        {t('reco.similarLoading')}
      </p>
    )
  }
  if (similar.data.items.length === 0) {
    return <p role="status" className="px-6 pb-6 pt-4 text-sm text-[var(--muted)]">{t('reco.similarEmpty')}</p>
  }

  const openTitle = (id: number) => {
    const next = new URLSearchParams(params)
    next.set('title', String(id))
    navigate({ pathname, search: next.toString() })
  }

  return (
    <section className="space-y-3 border-t border-white/10 px-6 pb-6 pt-4">
      <h3 className="text-sm font-medium uppercase tracking-wide text-[var(--muted)]">
        {t('reco.similarHeading')}
      </h3>
      <ul className="grid grid-cols-3 gap-3 sm:grid-cols-4 md:grid-cols-5 lg:grid-cols-6">
        {similar.data.items.map((item) => (
          <li key={item.id}>
            <button
              type="button"
              onClick={() => openTitle(item.id)}
              className="w-full text-left"
            >
              {item.posterUrl ? (
                <img
                  src={item.posterUrl}
                  alt=""
                  className="aspect-[2/3] w-full rounded-md object-cover"
                />
              ) : (
                <div className="aspect-[2/3] w-full rounded-md bg-white/5" />
              )}
              <p className="mt-1 line-clamp-2 text-xs leading-snug">{item.name}</p>
              {item.places && item.places.length > 0 && (
                <p className="mt-0.5 text-[10px] text-[var(--muted)]">
                  {item.places.map((p) => `${p.providerName} ${p.countryCode}`).join(' · ')}
                </p>
              )}
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
}
