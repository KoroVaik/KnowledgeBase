import { useEffect, useState } from 'react'
import { subscribeToConnection } from '../api/realtime'
import type { ConnectionStatus } from '../api/realtime'

/** Whether the change stream is up - the only thing on the page that keeps asking the API. */
export function useConnectionStatus(): ConnectionStatus {
  const [status, setStatus] = useState<ConnectionStatus>('paused')

  useEffect(() => subscribeToConnection(setStatus), [])

  return status
}
