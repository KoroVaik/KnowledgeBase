import { useEffect, useState } from 'react'
import { subscribeToConnection } from '../api/realtime'
import type { ConnectionState } from '../api/realtime'

interface ObservedConnectionState extends ConnectionState {
  observedAt: number
}

/** Whether the change stream is up - the only thing on the page that keeps asking the API. */
export function useConnectionStatus(): ObservedConnectionState {
  const [connection, setConnection] = useState<ObservedConnectionState>({
    status: 'paused',
    reconnectAt: null,
    observedAt: 0,
  })

  useEffect(
    () =>
      subscribeToConnection((next) => {
        setConnection({ ...next, observedAt: Date.now() })
      }),
    [],
  )

  return connection
}
