import { useMemo, useState } from 'react'
import type { DragEvent } from 'react'
import type { Tag } from '../../api/tags'

/** State and drag handlers behind TagHierarchyTree: which nodes are collapsed, which tag is
 *  mid-drag, and the per-row drop target. Dropping tag A onto tag B always *adds* B as a parent
 *  of A (never replaces one) - a tag is a DAG, see "Tag hierarchy" in docs/database.md. */
export function useTagHierarchyTree(tags: Tag[], onAddParent: (childId: string, parentId: string) => void) {
  const [collapsedIds, setCollapsedIds] = useState<Set<string>>(new Set())
  const [draggingId, setDraggingId] = useState<string | null>(null)
  const [dragOverId, setDragOverId] = useState<string | null>(null)

  const { roots, childrenByParent, byId } = useMemo(() => {
    const byId = new Map(tags.map((tag) => [tag.id, tag]))
    const childrenByParent = new Map<string, Tag[]>()

    for (const tag of tags) {
      for (const parentId of tag.parentIds) {
        const siblings = childrenByParent.get(parentId)
        if (siblings) {
          siblings.push(tag)
        } else {
          childrenByParent.set(parentId, [tag])
        }
      }
    }

    const roots = tags.filter((tag) => tag.parentIds.length === 0)

    return { roots, childrenByParent, byId }
  }, [tags])

  function toggle(tagId: string) {
    setCollapsedIds((current) => {
      const next = new Set(current)
      if (next.has(tagId)) {
        next.delete(tagId)
      } else {
        next.add(tagId)
      }
      return next
    })
  }

  function rowDragProps(tagId: string, disabled: boolean) {
    return {
      draggable: !disabled,
      onDragStart: (event: DragEvent) => {
        event.dataTransfer.setData('text/plain', tagId)
        setDraggingId(tagId)
      },
      onDragEnd: () => {
        setDraggingId(null)
        setDragOverId(null)
      },
      onDragOver: (event: DragEvent) => {
        if (draggingId !== null && draggingId !== tagId) {
          event.preventDefault()
        }
      },
      onDragEnter: () => {
        if (draggingId !== null && draggingId !== tagId) {
          setDragOverId(tagId)
        }
      },
      onDragLeave: (event: DragEvent) => {
        // dragleave also fires crossing onto a child (chevron, chip, count) - real leave only
        // if the cursor left the row itself, same fix as UploadDropZone's onDragLeave.
        if (event.currentTarget.contains(event.relatedTarget as Node | null)) {
          return
        }
        setDragOverId((current) => (current === tagId ? null : current))
      },
      onDrop: (event: DragEvent) => {
        event.preventDefault()
        const draggedId = event.dataTransfer.getData('text/plain')
        setDraggingId(null)
        setDragOverId(null)
        if (draggedId.length > 0 && draggedId !== tagId) {
          onAddParent(draggedId, tagId)
        }
      },
    }
  }

  return { roots, childrenByParent, byId, collapsedIds, toggle, draggingId, dragOverId, rowDragProps }
}
