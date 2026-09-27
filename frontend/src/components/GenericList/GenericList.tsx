import type { ReactNode } from 'react'
import { useGenericList } from './useGenericList'
import { AnimatedList } from './AnimatedList'
import './GenericList.css'
import type { ListUpdates } from '../../hooks/bufferedUpdates'

type ListState<T> = ReturnType<typeof useGenericList<T>>

interface GenericListProps<T> {
  items: readonly T[]
  listId: string
  filterKey?: string
  animated?: boolean
  updates?: ListUpdates
  children: (shownItems: readonly T[]) => ReactNode
}

export function GenericList<T>({ items, listId, filterKey, children, animated = false, updates }: GenericListProps<T>) {
  const list = useGenericList(items, listId, filterKey, updates)
  return <ControlledGenericList list={list} animated={animated}>{children}</ControlledGenericList>
}

export function ControlledGenericList<T>({ list, children, disabled = false, animated = false }: {
  list: ListState<T>
  children: (shownItems: readonly T[]) => ReactNode
  disabled?: boolean
  animated?: boolean
}) {
  return <>
    {list.newCount > 0 && <div className="generic-list-updates" role="status">
      <button type="button" className="btn btn-sm" disabled={disabled} onClick={list.showNew}>Show {list.newCount} new {list.newCount === 1 ? 'item' : 'items'}</button>
    </div>}
    {animated ? <AnimatedList resetKey={list.transitionKey}>{children(list.shownItems)}</AnimatedList> : children(list.shownItems)}
    {(list.moreCount > 0 || list.canShowLess) && <div className="generic-list-controls">
      {list.moreCount > 0 && <button type="button" className="btn btn-md" disabled={disabled} onClick={list.showMore}>Show {list.moreCount} more</button>}
      {list.canShowLess && <button type="button" className="btn btn-md" disabled={disabled} onClick={list.showLess}>Show less</button>}
    </div>}
  </>
}
