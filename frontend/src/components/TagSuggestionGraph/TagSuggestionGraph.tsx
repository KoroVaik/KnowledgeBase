import { useState } from 'react'
import './TagSuggestionGraph.css'

export interface MiniGraphCandidate {
  id: string
  name: string
  confidence: string
  confirmed: boolean
}

const NAME_CHAR_W = 7
const CONF_CHAR_W = 6.2
const PILL_PAD_X = 24
// A candidate pill is name + confidence (see .tag-mini-pill/.tag-confidence) - budgeted here
// only to *space* sibling candidates apart without overlap. Rendering itself is plain HTML at
// its natural size (no forced box), so an inexact estimate just crowds neighbours slightly -
// it can no longer clip text or push a button's hit area outside its own element.
const CONF_ALLOWANCE = 8
const CENTER_PAD_X = 22
const MIN_PILL_W = 64
const PILL_H = 30
const CENTER_H = 26
const ICON_D = 16
const ICON_GAP = 4
const NODE_GAP = 12
const TIER_GAP_Y = 18
const CANVAS_PAD = 6

interface Slot {
  candidate: MiniGraphCandidate
  x: number
}

interface FlipSpot {
  x: number
  y: number
}

interface Layout {
  width: number
  height: number
  centerX: number
  centerY: number
  parentSlots: Slot[]
  parentY: number
  childSlots: Slot[]
  childY: number
  parentEdges: string[]
  childEdges: string[]
  // Only set for a single-candidate tier - flipping with a sibling candidate still attached would
  // leave that sibling's edge pointing at a center that just changed identity.
  parentFlip: FlipSpot | null
  childFlip: FlipSpot | null
}

function centerPillWidth(name: string): number {
  return Math.max(MIN_PILL_W, CENTER_PAD_X + name.length * NAME_CHAR_W)
}

function candidatePillWidth(candidate: MiniGraphCandidate): number {
  return Math.max(
    MIN_PILL_W,
    PILL_PAD_X + candidate.name.length * NAME_CHAR_W + CONF_ALLOWANCE + candidate.confidence.length * CONF_CHAR_W,
  )
}

function slotWidth(candidate: MiniGraphCandidate): number {
  const pw = candidatePillWidth(candidate)
  return ICON_D + ICON_GAP + pw + ICON_GAP + ICON_D
}

function layoutTier(candidates: MiniGraphCandidate[], centerX: number): Slot[] {
  const widths = candidates.map((candidate) => slotWidth(candidate))
  const total = widths.reduce((sum, w) => sum + w, 0) + NODE_GAP * Math.max(0, candidates.length - 1)
  let cursor = centerX - total / 2

  return candidates.map((candidate, i) => {
    const width = widths[i]
    const x = cursor + width / 2
    cursor += width + NODE_GAP
    return { candidate, x }
  })
}

function edgePath(fromX: number, fromY: number, toX: number, toY: number): string {
  const midY = (fromY + toY) / 2
  return `M ${fromX},${fromY} Q ${fromX},${midY} ${toX},${toY}`
}

// Point at t=0.5 of the same quadratic bezier edgePath draws (control point at fromX, midY) -
// simplifies to this since the control point's Y is already the midpoint Y. Sits right on the
// edge on purpose - its two arrows straddle the line (see .tag-mini-flip) instead of covering it.
function flipSpot(fromX: number, fromY: number, toX: number, toY: number): FlipSpot {
  return { x: 0.75 * fromX + 0.25 * toX, y: (fromY + toY) / 2 }
}

// The span a tier actually needs, edge to edge - not just the distance between slot centres
// (that alone is 0 for a single centred candidate, understating the canvas width by that
// candidate's whole pill width and letting its reject button render past the row's own edge).
function tierSpan(candidates: MiniGraphCandidate[]): number {
  return layoutTier(candidates, 0).reduce(
    (max, slot) => Math.max(max, (Math.abs(slot.x) + slotWidth(slot.candidate) / 2) * 2),
    0,
  )
}

function computeLayout(
  centerName: string,
  parents: MiniGraphCandidate[],
  childCandidates: MiniGraphCandidate[],
): Layout {
  const centerWidth = centerPillWidth(centerName)

  // Lay each tier out around its own x = 0 first to measure how wide it wants to be, then
  // re-anchor every tier to one shared centreX - keeps the canvas only as wide as the widest
  // tier instead of a fixed size that wastes space for a 1-candidate row.
  const parentSpan = tierSpan(parents)
  const childSpan = tierSpan(childCandidates)

  const width = Math.max(centerWidth, parentSpan, childSpan) + CANVAS_PAD * 2
  const centerX = width / 2

  let y = CANVAS_PAD
  let parentY = 0
  if (parents.length > 0) {
    parentY = y + PILL_H / 2
    y += PILL_H + TIER_GAP_Y
  }
  const centerY = y + CENTER_H / 2
  y += CENTER_H
  let childY = 0
  if (childCandidates.length > 0) {
    y += TIER_GAP_Y
    childY = y + PILL_H / 2
    y += PILL_H
  }
  const height = y + CANVAS_PAD

  const parentSlots = layoutTier(parents, centerX)
  const childSlots = layoutTier(childCandidates, centerX)

  return {
    width,
    height,
    centerX,
    centerY,
    parentSlots,
    parentY,
    childSlots,
    childY,
    parentEdges: parentSlots.map((slot) => edgePath(centerX, centerY - CENTER_H / 2, slot.x, parentY + PILL_H / 2)),
    childEdges: childSlots.map((slot) => edgePath(centerX, centerY + CENTER_H / 2, slot.x, childY - PILL_H / 2)),
    parentFlip:
      parentSlots.length === 1
        ? flipSpot(centerX, centerY - CENTER_H / 2, parentSlots[0].x, parentY + PILL_H / 2)
        : null,
    childFlip:
      childSlots.length === 1
        ? flipSpot(centerX, centerY + CENTER_H / 2, childSlots[0].x, childY - PILL_H / 2)
        : null,
  }
}

/** Mini node-link view of one tag's pending suggestions: the tag in the centre, parent
 *  candidates above, child candidates below - the compact alternative to the flat chip rows
 *  (`TagSuggestionChip`), same data and callbacks, just laid out as a small graph. No
 *  pan/zoom/dagre - the shape is always at most a couple of candidates per side, so a fixed
 *  three-tier layout is enough. Parent and child candidates render identically (a plain label
 *  flanked by reject on the left, accept on the right) - an unconfirmed tag accepting its own
 *  suggested parent happens to also confirm the tag (see `confirmWithParent`), but that is a
 *  side effect of what "accept" wires to, not a different look.
 *
 *  Nodes are plain HTML, absolutely positioned at the computed coordinates rather than sized
 *  `<foreignObject>` boxes - a first version forced every chip into a pre-measured box and both
 *  clipped long names and pushed the accept/reject hit area outside the visible button whenever
 *  the estimate ran short. Positioning only (not sizing) tolerates an imprecise estimate: nodes
 *  always render at their natural size, worst case sitting a little close to a neighbour. */
export function TagSuggestionGraph({
  centerName,
  centerConfirmed,
  parents,
  childCandidates,
  onAcceptParent,
  onRejectParent,
  onAcceptChild,
  onRejectChild,
  onFlipAcceptParent,
  onFlipAcceptChild,
  disabled,
}: {
  centerName: string
  centerConfirmed: boolean
  parents: MiniGraphCandidate[]
  childCandidates: MiniGraphCandidate[]
  onAcceptParent: (id: string) => void
  onRejectParent: (id: string) => void
  onAcceptChild?: (id: string) => void
  onRejectChild?: (id: string) => void
  // Flip swaps the Y position of the center node and this tier's sole candidate - same two
  // rendered blocks, same styling each already had, just relocated. Accept then commits the
  // opposite direction from onAcceptParent/onAcceptChild; reject is unaffected (dismissing the
  // suggestion is the same action either way). The caller decides eligibility (this tier has
  // exactly one candidate and the other is empty) and only passes these down when it applies.
  onFlipAcceptParent?: (id: string) => void
  onFlipAcceptChild?: (id: string) => void
  disabled: boolean
}) {
  const [parentFlipped, setParentFlipped] = useState(false)
  const [childFlipped, setChildFlipped] = useState(false)

  if (parents.length === 0 && childCandidates.length === 0) {
    return null
  }

  const layout = computeLayout(centerName, parents, childCandidates)

  const flipParentActive = parentFlipped && parents.length === 1
  const flipChildActive = childFlipped && childCandidates.length === 1
  const centerY = flipParentActive ? layout.parentY : flipChildActive ? layout.childY : layout.centerY
  const parentY = flipParentActive ? layout.centerY : layout.parentY
  const childY = flipChildActive ? layout.centerY : layout.childY

  return (
    <div className="tag-mini-graph">
      <div className="tag-mini-canvas" style={{ width: layout.width, height: layout.height }}>
        <svg viewBox={`0 0 ${layout.width} ${layout.height}`} className="tag-mini-edges" role="img" aria-label={`Suggested placements for ${centerName}`}>
          {layout.parentSlots.map((slot, i) => (
            <path key={`pe-${slot.candidate.id}`} d={layout.parentEdges[i]} className="tag-mini-edge" />
          ))}
          {layout.childSlots.map((slot, i) => (
            <path key={`ce-${slot.candidate.id}`} d={layout.childEdges[i]} className="tag-mini-edge" />
          ))}
        </svg>

        {layout.parentFlip && onFlipAcceptParent !== undefined && (
          <button
            type="button"
            className="tag-mini-flip"
            style={{ left: layout.parentFlip.x, top: layout.parentFlip.y }}
            aria-label={`Swap ${centerName} and ${parents[0].name}`}
            disabled={disabled}
            onClick={() => setParentFlipped((current) => !current)}
          >
            <span aria-hidden="true">↑</span>
            <span aria-hidden="true">↓</span>
          </button>
        )}

        {layout.childFlip && onFlipAcceptChild !== undefined && (
          <button
            type="button"
            className="tag-mini-flip"
            style={{ left: layout.childFlip.x, top: layout.childFlip.y }}
            aria-label={`Swap ${centerName} and ${childCandidates[0].name}`}
            disabled={disabled}
            onClick={() => setChildFlipped((current) => !current)}
          >
            <span aria-hidden="true">↑</span>
            <span aria-hidden="true">↓</span>
          </button>
        )}

        <div
          className={
            centerConfirmed
              ? 'tag-chip chip-compact tag-mini-node tag-mini-center'
              : 'tag-chip tag-chip-unconfirmed chip-compact chip-review tag-mini-node tag-mini-center'
          }
          style={{ left: layout.centerX, top: centerY }}
        >
          {centerName}
        </div>

        {layout.parentSlots.map((slot) => (
          <div key={slot.candidate.id} className="tag-mini-node" style={{ left: slot.x, top: parentY }}>
            <div className="tag-mini-slot">
              <button
                type="button"
                className="tag-suggestion-reject"
                aria-label={`Reject ${slot.candidate.name}`}
                disabled={disabled}
                onClick={() => onRejectParent(slot.candidate.id)}
              >
                ×
              </button>
              <span
                className={
                  slot.candidate.confirmed ? 'tag-mini-pill tag-mini-pill-confirmed' : 'tag-mini-pill tag-mini-pill-unconfirmed'
                }
              >
                {slot.candidate.name}
                <span className="tag-confidence">{slot.candidate.confidence.toLowerCase()}</span>
              </span>
              <button
                type="button"
                className="tag-suggestion-accept"
                aria-label={`Accept ${slot.candidate.name}`}
                disabled={disabled}
                onClick={() =>
                  flipParentActive && onFlipAcceptParent !== undefined
                    ? onFlipAcceptParent(slot.candidate.id)
                    : onAcceptParent(slot.candidate.id)
                }
              >
                ✓
              </button>
            </div>
          </div>
        ))}

        {layout.childSlots.map((slot) => (
          <div key={slot.candidate.id} className="tag-mini-node" style={{ left: slot.x, top: childY }}>
            <div className="tag-mini-slot">
              <button
                type="button"
                className="tag-suggestion-reject"
                aria-label={`Reject ${slot.candidate.name}`}
                disabled={disabled}
                onClick={() => onRejectChild?.(slot.candidate.id)}
              >
                ×
              </button>
              <span
                className={
                  slot.candidate.confirmed ? 'tag-mini-pill tag-mini-pill-confirmed' : 'tag-mini-pill tag-mini-pill-unconfirmed'
                }
              >
                {slot.candidate.name}
                <span className="tag-confidence">{slot.candidate.confidence.toLowerCase()}</span>
              </span>
              <button
                type="button"
                className="tag-suggestion-accept"
                aria-label={`Accept ${slot.candidate.name}`}
                disabled={disabled}
                onClick={() =>
                  flipChildActive && onFlipAcceptChild !== undefined
                    ? onFlipAcceptChild(slot.candidate.id)
                    : onAcceptChild?.(slot.candidate.id)
                }
              >
                ✓
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
