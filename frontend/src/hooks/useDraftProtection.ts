import { createContext, useContext, useEffect, useId } from 'react'

type RegisterDraft = (id: string, dirty: boolean) => void
export const DraftProtection = createContext<RegisterDraft | null>(null)

export function useDraftProtection(dirty: boolean, register?: RegisterDraft) {
  const inherited = useContext(DraftProtection)
  const target = register ?? inherited
  const id = useId()
  useEffect(() => {
    target?.(id, dirty)
    return () => target?.(id, false)
  }, [dirty, id, target])
}
