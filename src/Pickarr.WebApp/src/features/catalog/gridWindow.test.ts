import { describe, expect, it } from 'vitest'
import { columnCount, dedupeById, visibleRowSpan } from './gridWindow'

describe('columnCount', () => {
  it('fits as many 180px cards as the row allows', () => {
    expect(columnCount(0)).toBe(1)
    expect(columnCount(180)).toBe(1)
    expect(columnCount(1024)).toBe(5)
  })
})

describe('visibleRowSpan', () => {
  it('keeps a window around the viewport', () => {
    const span = visibleRowSpan({
      scrollY: 0,
      viewportHeight: 400,
      listTop: 0,
      rowStride: 376,
      rowCount: 16,
      overscan: 3,
    })
    expect(span.start).toBe(0)
    expect(span.end).toBe(5)
  })

  it('shifts down with the scroll position', () => {
    const span = visibleRowSpan({
      scrollY: 2000,
      viewportHeight: 800,
      listTop: 200,
      rowStride: 400,
      rowCount: 40,
      overscan: 1,
    })
    expect(span.start).toBeGreaterThan(0)
    expect(span.end).toBeLessThan(40)
    expect(span.end).toBeGreaterThan(span.start)
  })
})

describe('dedupeById', () => {
  it('keeps the first copy of a repeated id', () => {
    const first = { id: 1, name: 'First' }
    const items = [first, { id: 2, name: 'Other' }, { id: 1, name: 'Again' }]
    expect(dedupeById(items)).toEqual([first, { id: 2, name: 'Other' }])
  })

  it('returns the same array when every id is unique', () => {
    const items = [{ id: 1 }, { id: 2 }]
    expect(dedupeById(items)).toBe(items)
  })
})
