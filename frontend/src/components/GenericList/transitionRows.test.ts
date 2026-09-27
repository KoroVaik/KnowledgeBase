import { expect, it } from 'vitest'
import { reconcileRows } from './transitionRows'
import type { TransitionRow } from './transitionRows'

function rows(...keys: string[]): TransitionRow<string>[] {
  return keys.map(key => ({ key, value: key, exiting: false, entering: false }))
}

const incoming = (...keys: string[]) => keys.map(key => ({ key, value: key }))

it('retains a removed row in its old position while the next page item enters', () => {
  const next = reconcileRows(rows('a', 'b', 'c'), incoming('a', 'c', 'd'))
  expect(next.map(row => row.key)).toEqual(['a', 'b', 'c', 'd'])
  expect(next.filter(row => row.exiting).map(row => row.key)).toEqual(['b'])
  expect(next.filter(row => row.entering).map(row => row.key)).toEqual(['d'])
})

it('keeps all bulk-deleted rows in order, including the last row', () => {
  const next = reconcileRows(rows('a', 'b', 'c', 'd'), incoming('b'))
  expect(next.map(row => row.key)).toEqual(['a', 'b', 'c', 'd'])
  expect(next.filter(row => row.exiting).map(row => row.key)).toEqual(['a', 'c', 'd'])
  expect(reconcileRows(rows('a'), []).map(row => [row.key, row.exiting])).toEqual([['a', true]])
})

it('does not duplicate exits when another server update arrives mid-transition', () => {
  const first = reconcileRows(rows('a', 'b', 'c'), incoming('a', 'c'))
  const second = reconcileRows(first, incoming('c', 'd'))
  expect(second.map(row => row.key)).toEqual(['a', 'b', 'c', 'd'])
  expect(second.filter(row => row.exiting).map(row => row.key)).toEqual(['a', 'b'])
})

it('cancels an exit when the same row reappears and refreshes its content', () => {
  const first = reconcileRows(rows('a', 'b'), incoming('b'))
  const next = reconcileRows(first, [{ key: 'a', value: 'updated' }, { key: 'b', value: 'b' }])
  expect(next[0]).toEqual({ key: 'a', value: 'updated', exiting: false, entering: false })
})

it('holds row positions during departures, then respects server reordering', () => {
  const next = reconcileRows(rows('a', 'b', 'c'), incoming('c', 'a'))
  expect(next.map(row => row.key)).toEqual(['a', 'b', 'c'])
  const finished = reconcileRows(next.filter(row => !row.exiting), incoming('c', 'a'))
  expect(finished.map(row => row.key)).toEqual(['c', 'a'])
  expect(next.every(row => !row.entering)).toBe(true)
})

it('keeps a new row fading in when a background refresh arrives', () => {
  const first = reconcileRows(rows('a'), incoming('a', 'b'))
  expect(reconcileRows(first, incoming('a', 'b'))[1].entering).toBe(true)
})
