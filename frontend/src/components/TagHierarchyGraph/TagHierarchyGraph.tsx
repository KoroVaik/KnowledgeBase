import type { CSSProperties } from 'react'
import type { Tag } from '../../api/tags'
import { notesText } from '../../format'
import { useTagHierarchyGraph } from './useTagHierarchyGraph'
import './TagHierarchyGraph.css'

/** Node-link view of the confirmed tag DAG - alternative to TagHierarchyTree's indented list.
 *  A tag with two parents shows up once here, with both edges converging into it, instead of
 *  once per branch (see TagHierarchyTree's own doc comment). Read-only: pan/zoom and a hover
 *  trace, no drag-to-reparent - that stays the tree's job. */
export function TagHierarchyGraph({ tags }: { tags: Tag[] }) {
  const {
    layout,
    scale,
    pan,
    containerRef,
    setHoveredId,
    connectedIds,
    onPointerDown,
    onPointerMove,
    onPointerUp,
  } = useTagHierarchyGraph(tags)

  if (tags.length === 0) {
    return <p className="tags-empty">No confirmed tags yet.</p>
  }

  return (
    <div
      className="tag-graph"
      ref={containerRef}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
      onPointerLeave={onPointerUp}
    >
      <svg
        viewBox={`0 0 ${Math.max(layout.width, 1)} ${Math.max(layout.height, 1)}`}
        className="tag-graph-svg"
        role="img"
        aria-label="Tag hierarchy as a graph"
      >
        <g transform={`translate(${pan.x} ${pan.y}) scale(${scale})`}>
          <g>
            {layout.edges.map((edge) => (
              <path
                key={edge.id}
                d={edge.path}
                className={
                  connectedIds === null || (connectedIds.has(edge.fromId) && connectedIds.has(edge.toId))
                    ? 'tag-graph-edge'
                    : 'tag-graph-edge tag-graph-edge-dim'
                }
              />
            ))}
          </g>

          <g>
            {layout.nodes.map((node) => {
              const style = { '--tag-node-color': `var(--tag-depth-${node.depth})` } as CSSProperties
              const dimmed = connectedIds !== null && !connectedIds.has(node.id)

              return (
                <g
                  key={node.id}
                  transform={`translate(${node.x - node.width / 2} ${node.y - node.height / 2})`}
                  className={dimmed ? 'tag-graph-node tag-graph-node-dim' : 'tag-graph-node'}
                  style={style}
                  onPointerEnter={() => setHoveredId(node.id)}
                  onPointerLeave={() => setHoveredId((current) => (current === node.id ? null : current))}
                >
                  <title>{`${node.name} · ${notesText(node.noteCount)}`}</title>
                  <rect width={node.width} height={node.height} rx={node.height / 2} />
                  <text x={node.width / 2} y={node.height / 2 + 4} textAnchor="middle">
                    {node.name}
                  </text>
                </g>
              )
            })}
          </g>
        </g>
      </svg>

      <p className="tag-graph-hint">Drag to pan · scroll to zoom · hover a tag to trace its links</p>
    </div>
  )
}
