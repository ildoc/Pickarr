import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { refreshTitleArr, setTitleSeasonMonitoring } from '../../api/arr'
import { fetchTitle } from '../../api/catalog'
import type { CastMember } from '../../api/types'
import { categoryLabelKey, formatDate, formatRuntime, formatYear } from '../../lib/format'
import { AddToArrDialog } from '../arr/AddToArrDialog'
import { DeleteFromArrDialog } from '../arr/DeleteFromArrDialog'
import { addWatched, removeWatched } from '../../api/watched'
import { FavoriteButton } from '../favorites/FavoriteButton'
import { RatingButtons } from '../ratings/RatingButtons'
import { useRatingMutation } from '../ratings/useRatingMutation'
import { SimilarRow } from '../reco/SimilarRow'
import { applyOptimisticWatched } from '../watched/watchedCache'
import { WatchedButton } from '../watched/WatchedButton'
import { ArrStatusBadge } from './ArrStatusBadge'
import { SeasonTable } from './SeasonTable'
import { SeriesSeasons } from './SeriesSeasons'

function getCastMembers(castMembers: CastMember[] | undefined, legacyCast: string[] | undefined) {
  const source = castMembers ?? (legacyCast ?? []).map((name) => ({
    name,
    photoUrl: null,
    imdbUrl: null,
  }))
  const seen = new Set<string>()
  return source.filter((member) => {
    const key = member.name.trim().toLocaleLowerCase()
    if (!key || seen.has(key)) return false
    seen.add(key)
    return true
  }).slice(0, 12)
}

export function TitleDialog({
  titleId,
  sourceId,
}: {
  titleId: number
  sourceId: number | null
}) {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const [params] = useSearchParams()
  const dialogRef = useRef<HTMLDialogElement>(null)
  const queryClient = useQueryClient()
  const [addOpen, setAddOpen] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)

  const detail = useQuery({
    queryKey: ['title', titleId, sourceId],
    queryFn: () => fetchTitle(titleId, sourceId ?? undefined),
  })

  const ratingMutation = useRatingMutation(titleId)

  const invalidateTitle = async () => {
    await queryClient.invalidateQueries({ queryKey: ['title', titleId] })
    await queryClient.invalidateQueries({ queryKey: ['titles'] })
  }

  const watchedMutation = useMutation({
    mutationFn: async (next: boolean) => {
      if (next) await addWatched(titleId)
      else await removeWatched(titleId)
    },
    onMutate: async (next) => {
      await queryClient.cancelQueries({ queryKey: ['recommendations'] })
      applyOptimisticWatched(queryClient, titleId, next)
    },
    onSuccess: async () => {
      await Promise.all([
        invalidateTitle(),
        queryClient.invalidateQueries({ queryKey: ['recommendations'] }),
        queryClient.invalidateQueries({ queryKey: ['reco-status'] }),
      ])
    },
  })

  const refresh = useMutation({
    mutationFn: () => refreshTitleArr(titleId),
    onSuccess: invalidateTitle,
  })

  const seasonMonitoring = useMutation({
    mutationFn: ({ seasonNumber, monitored }: { seasonNumber: number; monitored: boolean }) =>
      setTitleSeasonMonitoring(titleId, seasonNumber, monitored),
    onSuccess: invalidateTitle,
  })
  const castMembers = detail.data
    ? getCastMembers(detail.data.castMembers, detail.data.cast)
    : []

  useEffect(() => {
    const dialog = dialogRef.current
    if (!dialog) return
    if (!dialog.open) dialog.showModal()
    const onPointerDown = (event: MouseEvent) => {
      if (event.target === dialog) dialog.close()
    }
    dialog.addEventListener('mousedown', onPointerDown)
    const onClose = () => {
      const next = new URLSearchParams(params)
      next.delete('title')
      navigate({ pathname, search: next.toString() }, { replace: true })
    }
    dialog.addEventListener('close', onClose)
    return () => {
      dialog.removeEventListener('mousedown', onPointerDown)
      dialog.removeEventListener('close', onClose)
    }
  }, [navigate, params, pathname])

  useEffect(() => {
    if (!detail.data) return
    const onKeyDown = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null
      if (
        target &&
        (target.tagName === 'INPUT' ||
          target.tagName === 'TEXTAREA' ||
          target.tagName === 'SELECT' ||
          target.isContentEditable)
      ) {
        return
      }
      const key = event.key.toLowerCase()
      if (key !== 'l' && key !== 'd' && key !== 'w' && key !== 'o' && key !== 'n') return
      event.preventDefault()
      if (key === 'w') {
        watchedMutation.mutate(!(detail.data.isWatched ?? false))
        return
      }

      if (event.repeat || ratingMutation.isPending) return
      const current = detail.data.rating ?? null
      if (key === 'o') {
        ratingMutation.mutate(current === 'love' ? null : 'love')
      } else if (key === 'l') {
        ratingMutation.mutate(current === 'like' ? null : 'like')
      } else if (key === 'n') {
        ratingMutation.mutate(current === 'not_interested' ? null : 'not_interested')
      } else {
        ratingMutation.mutate(current === 'dislike' ? null : 'dislike')
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [detail.data, ratingMutation, watchedMutation])

  const close = () => dialogRef.current?.close()

  return (
    <dialog
      ref={dialogRef}
      className="m-auto w-[min(960px,92vw)] max-h-[90vh] overflow-y-auto rounded-xl border border-white/10 bg-[var(--surface)] p-0 text-[var(--text)] shadow-2xl backdrop:bg-black/70"
    >
      {detail.isLoading && (
        <p className="p-8 text-[var(--muted)]">{t('catalog.loading')}</p>
      )}
      {detail.isError && (
        <div className="p-8">
          <p>{t('title.notFound')}</p>
          <button type="button" className="mt-4 underline" onClick={close}>
            {t('title.close')}
          </button>
        </div>
      )}
      {detail.data && (
        <div>
          <div className="relative h-48 bg-black/50 md:h-64">
            {detail.data.backdropUrl && (
              <img
                src={detail.data.backdropUrl}
                alt=""
                className="h-full w-full object-cover opacity-70"
              />
            )}
            <button
              type="button"
              onClick={close}
              className="absolute right-3 top-3 rounded-md bg-black/60 px-3 py-1 text-sm"
            >
              {t('title.close')}
            </button>
          </div>
          <div className="grid gap-6 p-6 md:grid-cols-[180px_1fr]">
            <div>
              {detail.data.posterUrl ? (
                <img
                  src={detail.data.posterUrl}
                  alt=""
                  className="w-full rounded-lg shadow-lg"
                />
              ) : (
                <div className="aspect-[2/3] rounded-lg bg-white/5" />
              )}
            </div>
            <div className="space-y-4">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <h2 className="text-3xl font-semibold tracking-tight">{detail.data.name}</h2>
                  <p className="mt-1 text-[var(--muted)]">
                    {formatYear(detail.data.year)} · {t(categoryLabelKey(detail.data.category))}
                    {detail.data.seasonCount
                      ? ` · ${t('title.seasons', { count: detail.data.seasonCount })}`
                      : ''}
                    {detail.data.runtimeMinutes
                      ? ` · ${formatRuntime(detail.data.runtimeMinutes, i18n.language)}`
                      : ''}
                  </p>
                  {detail.data.originalTitle &&
                    detail.data.originalTitle !== detail.data.name && (
                      <p className="mt-1 text-sm text-[var(--muted)]">
                        {t('title.original')}: {detail.data.originalTitle}
                      </p>
                    )}
                </div>
                <div className="flex flex-wrap items-center justify-end gap-2">
                  <RatingButtons
                    titleId={detail.data.id}
                    rating={detail.data.rating ?? null}
                    variant="dialog"
                  />
                  <WatchedButton
                    titleId={detail.data.id}
                    isWatched={detail.data.isWatched ?? false}
                    variant="dialog"
                  />
                  <FavoriteButton
                    titleId={detail.data.id}
                    isFavorite={detail.data.isFavorite}
                  />
                </div>
              </div>

              {detail.data.arr.addBlockedReason !== 'notConfigured' && (
                <>
                  <section className="space-y-3 rounded-lg border border-white/10 bg-[var(--surface-2)] p-4">
                    <div className="flex flex-wrap items-center justify-between gap-3">
                      <h3 className="text-sm font-medium uppercase tracking-wide text-[var(--muted)]">
                        {t('title.library')}
                      </h3>
                      <button
                        type="button"
                        onClick={() => refresh.mutate()}
                        disabled={refresh.isPending}
                        className="rounded-md bg-white/10 px-2.5 py-1 text-xs hover:bg-white/15 disabled:opacity-50"
                      >
                        {t('title.refreshStatus')}
                      </button>
                    </div>
                    <div className="flex flex-wrap items-center gap-3">
                      <ArrStatusBadge state={detail.data.arr.state} progress={detail.data.arr.progress} />
                      {detail.data.arr.monitored != null && (
                        <span className="text-sm text-[var(--muted)]">
                          {detail.data.arr.monitored ? t('title.monitored') : '—'}
                        </span>
                      )}
                      {detail.data.arr.syncedAt && (
                        <span className="text-xs text-[var(--muted)]">
                          {t('title.syncedAt', {
                            date: formatDate(detail.data.arr.syncedAt, i18n.language),
                          })}
                        </span>
                      )}
                    </div>
                    {detail.data.arr.seasons && detail.data.arr.seasons.length > 0 && (
                      <>
                        <SeasonTable
                          seasons={detail.data.arr.seasons}
                          pendingSeason={seasonMonitoring.isPending
                            ? seasonMonitoring.variables?.seasonNumber ?? null
                            : null}
                          onToggleMonitoring={(seasonNumber, monitored) =>
                            seasonMonitoring.mutate({ seasonNumber, monitored })
                          }
                        />
                        {seasonMonitoring.isError && (
                          <p className="text-sm text-red-300">
                            {seasonMonitoring.error instanceof Error
                              ? seasonMonitoring.error.message
                              : t('arr.updateMonitoringFailed')}
                          </p>
                        )}
                      </>
                    )}
                    <div className="flex flex-wrap gap-2">
                      {detail.data.arr.canAdd && (
                        <button
                          type="button"
                          onClick={() => setAddOpen(true)}
                          className="rounded-md bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-500"
                        >
                          {t('arr.canAdd', {
                            kind: detail.data.arr.kind === 'radarr' ? 'Radarr' : 'Sonarr',
                          })}
                        </button>
                      )}
                      {detail.data.arr.canDelete && (
                        <button
                          type="button"
                          onClick={() => setDeleteOpen(true)}
                          className="rounded-md bg-white/10 px-3 py-1.5 text-sm hover:bg-white/15"
                        >
                          {t('arr.canDelete')}
                        </button>
                      )}
                      {detail.data.arr.addBlockedReason === 'missingTvdbId' && (
                        <span className="text-sm text-[var(--muted)]">{t('arr.missingTvdbId')}</span>
                      )}
                      {detail.data.arr.addBlockedReason === 'missingTmdbId' && (
                        <span className="text-sm text-[var(--muted)]">{t('arr.missingTmdbId')}</span>
                      )}
                    </div>
                  </section>

                  <AddToArrDialog
                    title={detail.data}
                    open={addOpen}
                    onClose={() => setAddOpen(false)}
                    onAdded={() => void invalidateTitle()}
                  />
                  <DeleteFromArrDialog
                    title={detail.data}
                    open={deleteOpen}
                    onClose={() => setDeleteOpen(false)}
                    onDeleted={() => void invalidateTitle()}
                  />
                </>
              )}

              {detail.data.genres.length > 0 && (
                <p className="text-sm text-[var(--muted)]">
                  {t('title.genres')}: {detail.data.genres.map((g) => g.name).join(', ')}
                </p>
              )}

              {detail.data.ratings.tmdb != null && (
                <p className="text-sm">
                  {t('title.ratingTmdb')}: {detail.data.ratings.tmdb.toFixed(1)}
                </p>
              )}

              {castMembers.length > 0 && (
                <section aria-labelledby="title-dialog-cast">
                  <h3
                    id="title-dialog-cast"
                    className="mb-2 text-sm font-medium uppercase tracking-wide text-[var(--muted)]"
                  >
                    {t('title.cast')}
                  </h3>
                  <div
                    className="flex gap-3 overflow-x-auto pb-1"
                    role="list"
                    aria-label={t('title.cast')}
                  >
                    {castMembers.map((member) => {
                      const card = (
                        <>
                          {member.photoUrl ? (
                            <img
                              src={member.photoUrl}
                              alt={member.name}
                              className="h-20 w-20 rounded-full object-cover"
                            />
                          ) : (
                            <div
                              aria-hidden="true"
                              className="flex h-20 w-20 items-center justify-center rounded-full bg-white/10 text-xl text-[var(--muted)]"
                            >
                              ?
                            </div>
                          )}
                          <span className="max-w-24 text-center text-xs">{member.name}</span>
                        </>
                      )

                      return (
                        <div
                          key={member.name}
                          role="listitem"
                          className="flex min-w-24 flex-col items-center gap-1"
                        >
                          {member.imdbUrl ? (
                            <a
                              href={member.imdbUrl}
                              target="_blank"
                              rel="noreferrer"
                              aria-label={t('title.openImdbFor', { name: member.name })}
                              className="flex flex-col items-center gap-1 rounded-md focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--accent)]"
                            >
                              {card}
                            </a>
                          ) : (
                            card
                          )}
                        </div>
                      )
                    })}
                  </div>
                </section>
              )}

              {detail.data.category === 'series' && (
                <SeriesSeasons titleId={detail.data.id} sourceId={sourceId} />
              )}

              {detail.data.overview && (
                <section>
                  <h3 className="mb-1 text-sm font-medium uppercase tracking-wide text-[var(--muted)]">
                    {t('title.overview')}
                  </h3>
                  <p className="leading-relaxed text-[var(--text)]/90">{detail.data.overview}</p>
                </section>
              )}

              <section>
                <h3 className="mb-1 text-sm font-medium uppercase tracking-wide text-[var(--muted)]">
                  {t('title.availability')}
                </h3>
                <ul className="space-y-1 text-sm">
                  {detail.data.availability.map((a) => (
                    <li key={a.sourceId}>
                      {a.countryCode}:{' '}
                      {a.removedAt
                        ? t('title.removedOn', { date: formatDate(a.removedAt, i18n.language) })
                        : a.addedAt
                          ? t('title.availableFrom', {
                              date: formatDate(a.addedAt, i18n.language),
                            })
                          : t('title.availableNow')}
                      {a.leavingDate
                        ? ` · ${t('title.leavingOn', { date: a.leavingDate })}`
                        : ''}
                    </li>
                  ))}
                </ul>
              </section>

              <section className="flex flex-wrap gap-3 text-sm">
                {detail.data.links.tmdb && (
                  <a className="underline" href={detail.data.links.tmdb} target="_blank" rel="noreferrer">
                    TMDB
                  </a>
                )}
                {detail.data.links.imdb && (
                  <a className="underline" href={detail.data.links.imdb} target="_blank" rel="noreferrer">
                    IMDb
                  </a>
                )}
                {detail.data.links.tvdb && (
                  <a className="underline" href={detail.data.links.tvdb} target="_blank" rel="noreferrer">
                    TVDB
                  </a>
                )}
              </section>
            </div>
          </div>
          <SimilarRow titleId={detail.data.id} sourceId={sourceId} />
        </div>
      )}
    </dialog>
  )
}
