import { describe, expect, it } from 'vitest'
import { migrateLegacyType } from './filterParams'

describe('migrateLegacyType', () => {
  it('turns the legacy stand-up type into the Stand-up genre', () => {
    const next = migrateLegacyType(new URLSearchParams('type=standup&q=live'))
    expect(next?.toString()).toBe('q=live&genre=-1')
  })

  it('keeps an explicit genre', () => {
    const next = migrateLegacyType(new URLSearchParams('type=standup&genre=35'))
    expect(next?.toString()).toBe('genre=35')
  })

  it('leaves current types alone', () => {
    expect(migrateLegacyType(new URLSearchParams('type=series'))).toBeNull()
  })
})
