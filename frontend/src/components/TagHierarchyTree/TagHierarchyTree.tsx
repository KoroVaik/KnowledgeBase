import type { CSSProperties } from 'react'
import type { Tag } from '../../api/tags'
import { notesText } from '../../format'
import { useTagHierarchyTree } from './useTagHierarchyTree'
import './TagHierarchyTree.css'

const DEPTH_COLORS = 6

/** An indented, collapsible view of the confirmed tag DAG - replaces the old per-row "Parents…"
 *  popover. A chevron opens what is below a tag (its children), matching the usual folder-tree
 *  reading; dragging a tag onto another adds the target as an extra parent, so a tag genuinely
 *  belonging under two branches (e.g. "Porsche" under both "Car" and "German brand") shows up
 *  under both rather than forcing a single home. See "Tag hierarchy" in docs/database.md. */
export function TagHierarchyTree({
  tags,
  disabled,
  onAddParent,
  onRemoveParent,
}: {
  tags: Tag[]
  disabled: boolean
  onAddParent: (childId: string, parentId: string) => void
  onRemoveParent: (childId: string, parentId: string) => void
}) {
  const { roots, childrenByParent, byId, collapsedIds, toggle, draggingId, dragOverId, rowDragProps } =
    useTagHierarchyTree(tags, onAddParent)

  function renderNode(tag: Tag, parentId: string | null, ancestry: readonly string[]) {
    if (ancestry.includes(tag.id)) {
      return null
    }

    const children = childrenByParent.get(tag.id) ?? []
    const hasChildren = children.length > 0
    const collapsed = collapsedIds.has(tag.id)
    const parentName = parentId !== null ? byId.get(parentId)?.name : undefined
    const depth = ancestry.length
    const nodeStyle = { '--tag-node-color': `var(--tag-depth-${depth % DEPTH_COLORS})` } as CSSProperties
    const nodeClassName = draggingId === tag.id ? 'tag-tree-node tag-tree-dragging' : 'tag-tree-node'

    return (
      <li key={parentId !== null ? `${parentId}:${tag.id}` : tag.id} className={nodeClassName} style={nodeStyle}>
        <div
          className={dragOverId === tag.id ? 'tag-tree-row tag-tree-drop-target' : 'tag-tree-row'}
          {...rowDragProps(tag.id, disabled)}
        >
          <button
            type="button"
            className="tag-tree-toggle"
            onClick={() => toggle(tag.id)}
            disabled={!hasChildren}
            aria-expanded={hasChildren ? !collapsed : undefined}
            aria-label={hasChildren ? `${collapsed ? 'Expand' : 'Collapse'} ${tag.name}` : undefined}
          >
            {hasChildren && (collapsed ? '▸' : '▾')}
          </button>

          <span className="tag-chip chip-compact">{tag.name}</span>

          <span className="tag-tree-meta">
            <span className="tags-count">{notesText(tag.noteCount)}</span>

            {parentId !== null && (
              <button
                type="button"
                className="tag-tree-remove"
                aria-label={`Remove ${parentName ?? 'parent'} as a parent of ${tag.name}`}
                onClick={() => onRemoveParent(tag.id, parentId)}
                disabled={disabled}
              >
                ×
              </button>
            )}
          </span>
        </div>

        {hasChildren && !collapsed && (
          <ul className="tag-tree-children">
            {children.map((child) => renderNode(child, tag.id, [...ancestry, tag.id]))}
          </ul>
        )}
      </li>
    )
  }

  if (roots.length === 0) {
    return <p className="tags-empty">No confirmed tags yet.</p>
  }

  return (
    <div className="tag-tree">
      <ul className="tag-tree-root">{roots.map((tag) => renderNode(tag, null, []))}</ul>
    </div>
  )
}
