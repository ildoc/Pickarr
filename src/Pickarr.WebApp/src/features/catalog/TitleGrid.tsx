import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TitleListItem } from '../../api/types'
import {
  ESTIMATED_ROW_PX,
  GRID_GAP_PX,
  columnCount,
  dedupeById,
  visibleRowSpan,
} from './gridWindow'
import { TitleCard } from './TitleCard'

type GridSpan = { columns: number; start: number; end: number }

function readSpan(itemCount: number, rowStride: number, node: HTMLDivElement | null): GridSpan {
  const width = node && node.clientWidth > 0 ? node.clientWidth : window.innerWidth || 1024
  const columns = columnCount(width)
  const rowCount = Math.ceil(itemCount / columns)
  const listTop = node ? node.getBoundingClientRect().top + window.scrollY : 0
  const span = visibleRowSpan({
    scrollY: window.scrollY || 0,
    viewportHeight: window.innerHeight || 800,
    listTop,
    rowStride,
    rowCount,
  })
  return { columns, start: span.start, end: span.end }
}

export function TitleGrid({
  items,
  hasMore,
  isFetchingNextPage,
  onLoadMore,
  showPlaces = false,
  dismissOnRate = false,
}: {
  items: TitleListItem[]
  hasMore: boolean
  isFetchingNextPage: boolean
  onLoadMore: () => void
  showPlaces?: boolean
  dismissOnRate?: boolean
}) {
  const { t } = useTranslation()
  const sentinel = useRef<HTMLDivElement | null>(null)
  const containerRef = useRef<HTMLDivElement | null>(null)
  const onLoadMoreRef = useRef(onLoadMore)
  useEffect(() => {
    onLoadMoreRef.current = onLoadMore
  }, [onLoadMore])

  const uniqueItems = useMemo(() => dedupeById(items), [items])
  const [rowStride, setRowStride] = useState(ESTIMATED_ROW_PX + GRID_GAP_PX)
  const [span, setSpan] = useState<GridSpan>(() =>
    readSpan(uniqueItems.length, ESTIMATED_ROW_PX + GRID_GAP_PX, null),
  )
  const strideRef = useRef(rowStride)
  const countRef = useRef(uniqueItems.length)
  const updateRef = useRef<() => void>(() => undefined)
  const widthRef = useRef(-1)

  const measureRow = useCallback((node: HTMLDivElement | null) => {
    if (!node) return
    const height = node.getBoundingClientRect().height
    if (height < 40) return
    const next = Math.round(height + GRID_GAP_PX)
    // Only grow. Shrinking on a shorter card restarts layout and can loop with scroll anchoring.
    setRowStride((prev) => (next > prev + 8 ? next : prev))
  }, [])

  useEffect(() => {
    const update = () => {
      const next = readSpan(countRef.current, strideRef.current, containerRef.current)
      setSpan((prev) =>
        prev.columns === next.columns && prev.start === next.start && prev.end === next.end
          ? prev
          : next,
      )
    }
    updateRef.current = update
    update()
    window.addEventListener('scroll', update, { passive: true })
    window.addEventListener('resize', update)
    let observer: ResizeObserver | undefined
    if (typeof ResizeObserver !== 'undefined' && containerRef.current) {
      widthRef.current = containerRef.current.clientWidth
      observer = new ResizeObserver(() => {
        const width = containerRef.current?.clientWidth ?? 0
        if (width === widthRef.current) return
        widthRef.current = width
        update()
      })
      observer.observe(containerRef.current)
    }
    return () => {
      window.removeEventListener('scroll', update)
      window.removeEventListener('resize', update)
      observer?.disconnect()
    }
  }, [])

  useEffect(() => {
    strideRef.current = rowStride
    countRef.current = uniqueItems.length
    updateRef.current()
  }, [uniqueItems.length, rowStride])

  useEffect(() => {
    const node = sentinel.current
    if (!node || !hasMore) return
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) onLoadMoreRef.current()
      },
      { rootMargin: '400px' },
    )
    observer.observe(node)
    return () => observer.disconnect()
  }, [hasMore])

  const rowCount = Math.ceil(uniqueItems.length / span.columns)
  const lastRow = Math.min(span.end, rowCount)
  const rows = []
  for (let index = span.start; index < lastRow; index += 1) {
    const from = index * span.columns
    rows.push({ index, items: uniqueItems.slice(from, from + span.columns) })
  }
  const listHeight = rowCount === 0 ? 0 : rowCount * rowStride - GRID_GAP_PX

  return (
    <div>
      {/* Fixed height, rows positioned inside. A spacer above the window grows on scroll and
          the browser's scroll anchoring feeds that growth back into setState until React aborts. */}
      <div
        ref={containerRef}
        className="relative"
        style={{ height: listHeight, overflowAnchor: 'none' }}
      >
        {rows.map((row) => (
          <div
            key={row.index}
            ref={row.index === span.start ? measureRow : undefined}
            className="absolute right-0 left-0 grid gap-4"
            style={{
              top: row.index * rowStride,
              gridTemplateColumns: `repeat(${span.columns}, minmax(0, 1fr))`,
            }}
          >
            {row.items.map((title) => (
              <TitleCard
                key={title.id}
                title={title}
                showPlaces={showPlaces}
                dismissOnRate={dismissOnRate}
              />
            ))}
          </div>
        ))}
      </div>
      <div ref={sentinel} className="h-8" />
      {isFetchingNextPage && (
        <p className="py-4 text-center text-sm text-[var(--muted)]">{t('catalog.loading')}</p>
      )}
      {hasMore && !isFetchingNextPage && (
        <div className="flex justify-center py-2">
          <button
            type="button"
            onClick={onLoadMore}
            className="rounded-md bg-white/5 px-4 py-2 text-sm text-[var(--muted)] hover:bg-white/10"
          >
            {t('catalog.loadMore')}
          </button>
        </div>
      )}
    </div>
  )
}
