import { useState } from 'react'

export function useListPresence(hasItems: boolean) {
  const [wasPresent, setWasPresent] = useState(hasItems)
  if (hasItems && !wasPresent) setWasPresent(true)
  return hasItems || wasPresent
}
