export interface TransitionRow<T> {
  key: string
  value: T
  exiting: boolean
  entering: boolean
}

export function reconcileRows<T>(previous: readonly TransitionRow<T>[], incoming: readonly { key: string; value: T }[]): TransitionRow<T>[] {
  const previousByKey = new Map(previous.map(row => [row.key, row]))
  const incomingByKey = new Map(incoming.map(row => [row.key, row]))
  const liveRows = incoming.map(row => ({ ...row, exiting: false, entering: previousByKey.get(row.key)?.entering ?? true }))
  if (!previous.some(row => !incomingByKey.has(row.key))) return liveRows

  // Defer server reordering until departures finish so the clicked row stays in its place.
  return [
    ...previous.map(row => {
      const current = incomingByKey.get(row.key)
      return current
        ? { ...row, value: current.value, exiting: false }
        : { ...row, exiting: true, entering: false }
    }),
    ...liveRows.filter(row => !previousByKey.has(row.key)),
  ]
}
