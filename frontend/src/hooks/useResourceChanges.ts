import { useEffect, useRef } from 'react'
import { subscribeToChanges } from '../api/realtime'

/** Calls `onChange` when the server reports this collection changed, and after a reconnect.
 *  An event says what changed, not to what - so re-read the collection. */
export function useResourceChanges(resource: string, onChange: () => void) {
  // In a ref so a handler rebuilt each render does not re-open the stream.
  const handler = useRef(onChange)

  useEffect(() => {
    handler.current = onChange
  })

  useEffect(() => subscribeToChanges(resource, () => handler.current()), [resource])
}
