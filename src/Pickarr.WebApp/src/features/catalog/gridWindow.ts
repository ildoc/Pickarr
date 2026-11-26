/** Matches `minmax(180px, 1fr)` + `gap-4` in the poster grid. */
export const CARD_MIN_PX = 180
export const GRID_GAP_PX = 16
export const ESTIMATED_ROW_PX = 320
export const OVERSCAN_ROWS = 3

export function columnCount(width: number): number {
  if (width <= 0) return 1
  return Math.max(1, Math.floor((width + GRID_GAP_PX) / (CARD_MIN_PX + GRID_GAP_PX)))
}

/** Rows that intersect the viewport, plus a few above and below. */
export function visibleRowSpan(options: {
  scrollY: number
  viewportHeight: number
  listTop: number
  rowStride: number
  rowCount: number
  overscan?: number
}): { start: number; end: number } {
  const overscan = options.overscan ?? OVERSCAN_ROWS
  const stride = Math.max(1, options.rowStride)
  const rowCount = Math.max(0, options.rowCount)
  if (rowCount === 0) return { start: 0, end: 0 }

  const viewTop = options.scrollY - options.listTop
  const viewBottom = viewTop + Math.max(0, options.viewportHeight)
  const start = Math.max(0, Math.floor(viewTop / stride) - overscan)
  const end = Math.min(rowCount, Math.max(start, Math.ceil(viewBottom / stride) + overscan))
  return { start, end }
}

/**
 * Row pitch from a measured card row. Ignore a tiny or empty box, and never shrink:
 * a shorter card plus scroll anchoring restarts layout.
 */
export function nextRowStride(previous: number, measuredHeight: number, gap = GRID_GAP_PX): number {
  if (measuredHeight < 40) return previous
  const next = Math.round(measuredHeight + gap)
  return next > previous + 8 ? next : previous
}

/** Keep the first copy when a later page repeats an id already on screen. */
export function dedupeById<T extends { id: number }>(items: readonly T[]): T[] {
  const seen = new Set<number>()
  const unique: T[] = []
  for (const item of items) {
    if (seen.has(item.id)) continue
    seen.add(item.id)
    unique.push(item)
  }
  return unique.length === items.length ? (items as T[]) : unique
}
