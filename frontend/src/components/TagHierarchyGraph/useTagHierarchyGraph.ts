import { useMemo, useRef, useState } from 'react'
import type { PointerEvent as ReactPointerEvent } from 'react'
import dagre from '@dagrejs/dagre'
import type { Tag } from '../../api/tags'

const NODE_HEIGHT = 34
const CHAR_WIDTH = 7
const NODE_PADDING_X = 28
const MIN_NODE_WIDTH = 64
const RANK_SEP = 56
const NODE_SEP = 24
const DEPTH_COLORS = 6
const MIN_SCALE = 0.4
const MAX_SCALE = 2.5
const ZOOM_STEP = 0.0015

export interface GraphNode {
  id: string
  name: string
  noteCount: number
  x: number
  y: number
  width: number
  height: number
  depth: number
}

export interface GraphEdge {
  id: string
  fromId: string
  toId: string
  path: string
}

interface Layout {
  nodes: GraphNode[]
  edges: GraphEdge[]
  width: number
  height: number
}

const EMPTY_LAYOUT: Layout = { nodes: [], edges: [], width: 0, height: 0 }

/** Positions the confirmed tag DAG with dagre (a layered/Sugiyama layout: tiers by longest
 *  path from a root, minimal edge crossings within a tier) instead of a hand-rolled one - a
 *  tag with two parents needs real crossing-minimisation to stay legible, which dagre already
 *  solves. Only the layout (x/y, edge routing points) comes from it; rendering is plain SVG in
 *  TagHierarchyGraph.tsx, coloured from the same --tag-depth-N cycle as TagHierarchyTree. */
function computeLayout(tags: Tag[]): Layout {
  if (tags.length === 0) {
    return EMPTY_LAYOUT
  }

  const graph = new dagre.graphlib.Graph()
  graph.setGraph({
    rankdir: 'TB',
    ranker: 'longest-path',
    nodesep: NODE_SEP,
    ranksep: RANK_SEP,
    marginx: 16,
    marginy: 16,
  })
  graph.setDefaultEdgeLabel(() => ({}))

  const idsInGraph = new Set(tags.map((tag) => tag.id))

  for (const tag of tags) {
    const width = Math.max(MIN_NODE_WIDTH, NODE_PADDING_X + tag.name.length * CHAR_WIDTH)
    graph.setNode(tag.id, { width, height: NODE_HEIGHT })
  }

  for (const tag of tags) {
    for (const parentId of tag.parentIds) {
      // A pending/unconfirmed parent never reaches this view (only confirmed tags are passed
      // in), so its edge would dangle - drop it rather than let dagre throw on a missing node.
      if (idsInGraph.has(parentId)) {
        graph.setEdge(parentId, tag.id)
      }
    }
  }

  dagre.layout(graph)

  const byId = new Map(tags.map((tag) => [tag.id, tag]))
  const nodes: GraphNode[] = graph.nodes().map((id) => {
    const label = graph.node(id)
    const tag = byId.get(id)
    return {
      id,
      name: tag?.name ?? id,
      noteCount: tag?.noteCount ?? 0,
      x: label.x ?? 0,
      y: label.y ?? 0,
      width: label.width,
      height: label.height,
      depth: (label.rank ?? 0) % DEPTH_COLORS,
    }
  })

  const edges: GraphEdge[] = graph.edges().map((edge) => ({
    id: `${edge.v}:${edge.w}`,
    fromId: edge.v,
    toId: edge.w,
    path: smoothPath(graph.edge(edge).points ?? []),
  }))

  const graphLabel = graph.graph()
  return { nodes, edges, width: graphLabel.width ?? 0, height: graphLabel.height ?? 0 }
}

// A quadratic curve through the midpoint between each pair of routing points - smooths
// dagre's straight-segment polyline without pulling in a curve-fitting dependency for what is
// usually two or three points.
function smoothPath(points: { x: number; y: number }[]): string {
  if (points.length === 0) {
    return ''
  }
  if (points.length === 1) {
    return `M ${points[0].x},${points[0].y}`
  }

  let d = `M ${points[0].x},${points[0].y}`
  for (let i = 1; i < points.length - 1; i++) {
    const curr = points[i]
    const next = points[i + 1]
    d += ` Q ${curr.x},${curr.y} ${(curr.x + next.x) / 2},${(curr.y + next.y) / 2}`
  }
  const last = points[points.length - 1]
  d += ` L ${last.x},${last.y}`
  return d
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value))
}

/** State and pointer/wheel handlers behind TagHierarchyGraph: the dagre layout, current pan/
 *  zoom, and which tag is hovered (so its edges and neighbours can be traced). View-only - no
 *  drag-to-reparent here, that stays TagHierarchyTree's job. */
export function useTagHierarchyGraph(tags: Tag[]) {
  const layout = useMemo(() => computeLayout(tags), [tags])

  const [scale, setScale] = useState(1)
  const [pan, setPan] = useState({ x: 0, y: 0 })
  const [hoveredId, setHoveredId] = useState<string | null>(null)
  const containerRef = useRef<HTMLDivElement | null>(null)
  const dragState = useRef<{
    pointerId: number
    startX: number
    startY: number
    originX: number
    originY: number
  } | null>(null)

  // React attaches onWheel as a passive listener on the root, so a JSX handler's
  // preventDefault() is silently ignored (and warns in dev) - it never stops the page from
  // scrolling while the pointer is over the graph. A native listener with { passive: false },
  // wired through the ref callback so it attaches as soon as the div mounts, is the way round
  // it - same class of gotcha as the upload drop zone's window-level drag handlers.
  function attachWheelListener(node: HTMLDivElement | null) {
    containerRef.current?.removeEventListener('wheel', onWheel)
    containerRef.current = node
    node?.addEventListener('wheel', onWheel, { passive: false })
  }

  function onWheel(event: WheelEvent) {
    event.preventDefault()
    setScale((current) => clamp(current - event.deltaY * ZOOM_STEP, MIN_SCALE, MAX_SCALE))
  }

  function onPointerDown(event: ReactPointerEvent<HTMLDivElement>) {
    if (event.button !== 0) {
      return
    }
    event.currentTarget.setPointerCapture(event.pointerId)
    dragState.current = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      originX: pan.x,
      originY: pan.y,
    }
  }

  function onPointerMove(event: ReactPointerEvent<HTMLDivElement>) {
    const drag = dragState.current
    if (drag === null || drag.pointerId !== event.pointerId) {
      return
    }
    setPan({ x: drag.originX + (event.clientX - drag.startX), y: drag.originY + (event.clientY - drag.startY) })
  }

  function onPointerUp(event: ReactPointerEvent<HTMLDivElement>) {
    if (dragState.current?.pointerId === event.pointerId) {
      dragState.current = null
    }
  }

  const connectedIds = useMemo(() => {
    if (hoveredId === null) {
      return null
    }
    const ids = new Set([hoveredId])
    for (const edge of layout.edges) {
      if (edge.fromId === hoveredId) {
        ids.add(edge.toId)
      }
      if (edge.toId === hoveredId) {
        ids.add(edge.fromId)
      }
    }
    return ids
  }, [hoveredId, layout.edges])

  return {
    layout,
    scale,
    pan,
    containerRef: attachWheelListener,
    hoveredId,
    setHoveredId,
    connectedIds,
    onPointerDown,
    onPointerMove,
    onPointerUp,
  }
}
