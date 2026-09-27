import { useEffect } from 'react'
import { usePreference } from '../../preferences/usePreference'
import { setPreference } from '../../preferences/preferences'
import type { ListUpdates } from '../../hooks/bufferedUpdates'

const PAGE_SIZE = 5

interface ListPreference {
  filterKey: string
  limit: number
}

function isListPreference(value: unknown): value is ListPreference {
  return typeof value === 'object' && value !== null &&
    'filterKey' in value && typeof value.filterKey === 'string' &&
    'limit' in value && typeof value.limit === 'number' &&
    Number.isSafeInteger(value.limit) && value.limit >= PAGE_SIZE
}

export function useGenericList<T>(items: readonly T[], listId: string, filterKey = '', updates?: ListUpdates) {
  const preferenceKey = `list:${listId.toLowerCase()}`
  const [preference, update] = usePreference<ListPreference>(
    preferenceKey, { filterKey, limit: PAGE_SIZE }, isListPreference,
  )
  const limit = preference.filterKey === filterKey ? preference.limit : PAGE_SIZE

  useEffect(() => {
    if (preference.filterKey !== filterKey) {
      setPreference(preferenceKey, { filterKey, limit: PAGE_SIZE })
    }
  }, [filterKey, preference.filterKey, preferenceKey])

  const shownItems = items.slice(0, limit)
  return {
    newCount: updates?.count ?? 0,
    showNew: () => {
      if (!updates) return
      update({ filterKey, limit: Math.max(limit + updates.count, updates.revealCount) })
      updates.show()
    },
    transitionKey: JSON.stringify([filterKey, limit]),
    shownItems,
    moreCount: Math.min(PAGE_SIZE, items.length - shownItems.length),
    canShowLess: shownItems.length > PAGE_SIZE,
    showMore: () => update({ filterKey, limit: shownItems.length + PAGE_SIZE }),
    showLess: () => update({ filterKey, limit: PAGE_SIZE }),
    reveal: (index: number) => {
      if (index >= shownItems.length && index < items.length) {
        update({ filterKey, limit: Math.ceil((index + 1) / PAGE_SIZE) * PAGE_SIZE })
      }
    },
  }
}
