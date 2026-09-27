export function itemKey(item: unknown): string | null {
  if (typeof item !== 'object' || item === null) return null
  const value = item as Record<string, unknown>
  for (const field of ['id', 'candidateId', 'personId', 'clusterId', 'groupId', 'storedFileName']) {
    if (typeof value[field] === 'string') return value[field]
  }
  return null
}

export function sameData(a: unknown, b: unknown): boolean {
  return JSON.stringify(a) === JSON.stringify(b)
}

export function collectItemKeys(value: unknown, keys = new Set<string>()): Set<string> {
  if (value && typeof value === 'object') {
    const id = itemKey(value)
    if (id !== null) keys.add(id)
    for (const child of Object.values(value)) collectItemKeys(child, keys)
  }
  return keys
}

/** Insert around surviving neighbours without moving or replacing the rows being read. */
export function insertNewItems<T>(shown: readonly T[], latest: readonly T[], key: (item: T) => string | null = itemKey): T[] {
  const result = [...shown]
  const present = new Set(shown.map(key))
  for (let index = 0; index < latest.length; index++) {
    const item = latest[index]
    const id = key(item)
    if (id === null || present.has(id)) continue
    const next = latest.slice(index + 1).find(candidate => present.has(key(candidate)))
    const position = next === undefined ? result.length : result.findIndex(candidate => key(candidate) === key(next))
    result.splice(position, 0, item)
    present.add(id)
  }
  return result
}

export function mergeAccepted<T>(shown: T, latest: T, ids: ReadonlySet<string>, additionsOnly = false): T {
  if (Array.isArray(shown) && Array.isArray(latest)) {
    const nextById = new Map(latest.map(item => [itemKey(item), item]))
    const kept = additionsOnly ? shown : shown.flatMap(item => {
      const id = itemKey(item)
      if (id === null || !ids.has(id)) return [item]
      return nextById.has(id) ? [nextById.get(id)] : []
    })
    const candidates = latest.filter(item => {
      const id = itemKey(item)
      return id !== null && (ids.has(id) || kept.some(old => itemKey(old) === id))
    })
    return insertNewItems(kept, candidates) as T
  }
  if (shown && latest && typeof shown === 'object' && typeof latest === 'object') {
    const result = { ...shown }
    for (const key of Object.keys(shown) as (keyof T)[]) {
      if (typeof shown[key] === 'object' && shown[key] !== null && latest[key] !== undefined) {
        result[key] = mergeAccepted(shown[key], latest[key], ids, additionsOnly)
      }
    }
    return result
  }
  return shown
}

export function hasExistingChanges(shown: unknown, latest: unknown): boolean {
  if (Array.isArray(shown) && Array.isArray(latest)) {
    if (shown.some(item => itemKey(item) === null) || latest.some(item => itemKey(item) === null)) return !sameData(shown, latest)
    const oldIds = new Set(shown.map(itemKey))
    return !sameData(shown, latest.filter(item => oldIds.has(itemKey(item))))
  }
  if (shown && latest && typeof shown === 'object' && typeof latest === 'object') {
    return Object.keys(shown).some(key => hasExistingChanges(
      (shown as Record<string, unknown>)[key], (latest as Record<string, unknown>)[key],
    ))
  }
  return !sameData(shown, latest)
}

export interface ListUpdates {
  count: number
  revealCount: number
  show: () => void
}
