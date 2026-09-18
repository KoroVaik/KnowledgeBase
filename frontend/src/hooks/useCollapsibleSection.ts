import { usePreference } from '../preferences/usePreference'

function isBoolean(value: unknown): value is boolean {
  return typeof value === 'boolean'
}

/** Collapsed state for a page section or subsection, saved per user on the server. */
export function useCollapsibleSection(key: string, defaultCollapsed = true) {
  const [collapsed, setCollapsed] = usePreference(`section-collapsed:${key}`, defaultCollapsed, isBoolean)

  function toggle() {
    setCollapsed(!collapsed)
  }

  return { collapsed, toggle }
}
