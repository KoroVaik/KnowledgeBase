import { describe, expect, it } from 'vitest'
import { collectItemKeys, hasExistingChanges, insertNewItems, mergeAccepted } from './bufferedUpdates'

const row = (id: string, text = id) => ({ id, text })

describe('buffered list updates', () => {
  it('inserts new items in server order without replacing, deleting or reordering existing rows', () => {
    const shown = [row('a'), row('b'), row('c')]
    const latest = [row('first'), row('a', 'edited'), row('middle'), row('c'), row('last')]
    const next = insertNewItems(shown, latest)
    expect(next.map(item => item.id)).toEqual(['first', 'a', 'b', 'middle', 'c', 'last'])
    expect(next[1]).toBe(shown[0])
    expect(next[2]).toBe(shown[1])
  })

  it('deduplicates repeated events and keeps multiple new items in order', () => {
    const latest = [row('a'), row('b'), row('b'), row('c')]
    const result = insertNewItems([row('c')], latest)
    expect(result.map(item => item.id)).toEqual(['a', 'b', 'c'])
    expect(insertNewItems(result, latest)).toEqual(result)
  })

  it('accepts only the clicked additions while preserving pending edits and removals', () => {
    const shown = { rows: [row('a'), row('b')], count: 2 }
    const latest = { rows: [row('new'), row('a', 'edited'), row('another')], count: 3 }
    expect(mergeAccepted(shown, latest, new Set(['new']), true)).toEqual({
      rows: [row('new'), row('a'), row('b')], count: 2,
    })
  })

  it('does not resurrect a buffered addition deleted before the click is processed', () => {
    const shown = { rows: [row('old')] }
    expect(mergeAccepted(shown, shown, new Set(['removed']), true)).toEqual(shown)
  })

  it('applies a local action only to its affected rows, including movement between lists', () => {
    const shown = { notes: [row('a'), row('b')], bin: [] as ReturnType<typeof row>[] }
    const latest = { notes: [row('new'), row('b', 'edited elsewhere')], bin: [row('a')] }
    expect(mergeAccepted(shown, latest, new Set(['a']))).toEqual({ notes: [row('b')], bin: [row('a')] })
  })

  it('never appends new faces to an existing review group when accepting another group', () => {
    const oldGroup = { clusterId: 'group', faces: [{ candidateId: 'face' }] }
    const shown = { groups: [oldGroup] }
    const latest = { groups: [
      { clusterId: 'group', faces: [{ candidateId: 'face' }, { candidateId: 'unseen' }] },
      { clusterId: 'new-group', faces: [{ candidateId: 'new-face' }] },
    ] }
    const result = mergeAccepted(shown, latest, new Set(['new-group']), true)
    expect(result.groups[0]).toBe(oldGroup)
    expect(result.groups[1]).toEqual(latest.groups[1])
    expect(hasExistingChanges(result, latest)).toBe(true)
  })

  it('recognizes reused faces across different group IDs', () => {
    const keys = collectItemKeys({ groups: [{ clusterId: 'old', faces: [{ candidateId: 'face' }] }] })
    const regrouped = collectItemKeys({ clusterId: 'new', faces: [{ candidateId: 'face' }] })
    expect([...regrouped].some(id => keys.has(id))).toBe(true)
  })

  it('shows Reload for edits, deletions or reordering, but not for additions alone', () => {
    const shown = { rows: [row('a'), row('b')] }
    expect(hasExistingChanges(shown, { rows: [row('new'), ...shown.rows] })).toBe(false)
    expect(hasExistingChanges(shown, { rows: [row('a', 'edited'), row('b')] })).toBe(true)
    expect(hasExistingChanges(shown, { rows: [row('a')] })).toBe(true)
    expect(hasExistingChanges(shown, { rows: [row('b'), row('a')] })).toBe(true)
  })
})
