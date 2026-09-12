import { useState } from 'react'

const STORAGE_PREFIX = 'kb.section-collapsed.'

/** Collapsed state for a page section or subsection, kept in localStorage so it survives
 *  a reload - per-tab UI state, not data, so it does not belong in the API. */
export function useCollapsibleSection(key: string) {
  const [collapsed, setCollapsed] = useState(() => {
    try {
      return localStorage.getItem(STORAGE_PREFIX + key) === '1'
    } catch {
      return false
    }
  })

  function toggle() {
    setCollapsed((current) => {
      const next = !current
      try {
        localStorage.setItem(STORAGE_PREFIX + key, next ? '1' : '0')
      } catch {
        // Private browsing / storage denial - collapsing still works, just not remembered.
      }
      return next
    })
  }

  return { collapsed, toggle }
}
