import { useEffect, useState } from 'react'

/** Current time in ms, refreshed every `intervalMs` - keeps "X minutes ago" text moving. */
export function useNow(intervalMs: number): number {
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), intervalMs)
    return () => window.clearInterval(timer)
  }, [intervalMs])

  return now
}
