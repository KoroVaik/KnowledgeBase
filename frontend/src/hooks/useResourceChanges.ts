import { useEffect, useRef } from 'react'
import { subscribeToChanges } from '../api/realtime'

/**
 * Calls `onChange` whenever the server reports that this collection changed, and after the
 * stream reconnects. The argument is not passed on: an event says what changed, never what it
 * changed to, so the only correct reaction is to re-read the collection.
 */
export function useResourceChanges(resource: string, onChange: () => void) {
  // Held in a ref so that a handler rebuilt on every render does not close the stream and
  // open it again on every render with it.
  const handler = useRef(onChange)

  useEffect(() => {
    handler.current = onChange
  })

  useEffect(() => subscribeToChanges(resource, () => handler.current()), [resource])
}
