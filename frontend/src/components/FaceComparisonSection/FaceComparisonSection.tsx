import { useState } from 'react'
import type { AssetSummary } from '../../api/assets'
import type { ComparisonDetection, ComparisonResult, ComparisonRun } from '../../api/faceComparisons'
import type { FaceBounds } from '../../api/photoAnalysis'
import { useCollapsibleSection } from '../../hooks/useCollapsibleSection'
import { FaceCropPreview, FullPhotoPreview, PhotoPopupDialog, type FaceOverlay, type PhotoPopupTarget } from '../FacePreview/FacePreview'
import { PhotoMultiSelect } from '../MultiSelect/PhotoMultiSelect'
import { Select } from '../Select/Select'
import { useFaceComparison } from './useFaceComparison'
import './FaceComparisonSection.css'

const modelColors = ['#38bdf8', '#c084fc', '#fbbf24']
const modelLetters = ['Y', 'S', 'U']
type GroupItem = { result: ComparisonResult; detection: ComparisonDetection; modelIndex: number }
type CandidateGroup = { id: string; number: number; items: GroupItem[]; bounds: FaceBounds }

function overlap(left: FaceBounds, right: FaceBounds) {
  const width = Math.max(0, Math.min(left.x + left.width, right.x + right.width) - Math.max(left.x, right.x))
  const height = Math.max(0, Math.min(left.y + left.height, right.y + right.height) - Math.max(left.y, right.y))
  return width * height / (left.width * left.height + right.width * right.height - width * height) || 0
}
function groupBounds(items: GroupItem[]): FaceBounds {
  const left = Math.min(...items.map(item => item.detection.bounds.x)), top = Math.min(...items.map(item => item.detection.bounds.y))
  const right = Math.max(...items.map(item => item.detection.bounds.x + item.detection.bounds.width)), bottom = Math.max(...items.map(item => item.detection.bounds.y + item.detection.bounds.height))
  return { x: left, y: top, width: right - left, height: bottom - top }
}
function candidateGroups(run: ComparisonRun): CandidateGroup[] {
  const items = run.results.flatMap((result, modelIndex) => result.error || result.completedAtUtc === null ? [] : result.detections.map(detection => ({ result, detection, modelIndex })))
  const parent = items.map((_, index) => index)
  const root = (index: number): number => parent[index] === index ? index : parent[index] = root(parent[index])
  const join = (left: number, right: number) => { const leftRoot = root(left), rightRoot = root(right); if (leftRoot !== rightRoot) parent[rightRoot] = leftRoot }
  for (let left = 0; left < items.length; left++) for (let right = left + 1; right < items.length; right++) if (overlap(items[left].detection.bounds, items[right].detection.bounds) >= 0.4) join(left, right)
  const grouped = new Map<number, GroupItem[]>()
  items.forEach((item, index) => { const key = root(index); grouped.set(key, [...(grouped.get(key) ?? []), item]) })
  return [...grouped.values()].sort((left, right) => { const a = groupBounds(left), b = groupBounds(right); return a.y - b.y || a.x - b.x }).map((items, index) => ({ id: `group-${index + 1}`, number: index + 1, items, bounds: groupBounds(items) }))
}
function groupIsApproved(group: CandidateGroup) { return group.items.some(item => item.detection.isFace !== false) }
function groupIsRejected(group: CandidateGroup) { return group.items.every(item => item.detection.isFace === false) }
function groupState(group: CandidateGroup) { return groupIsApproved(group) ? 'approved' : groupIsRejected(group) ? 'rejected' : 'unreviewed' }
function isAgreed(group: CandidateGroup, run: ComparisonRun) {
  const expected = run.results.filter(result => result.error === null && result.completedAtUtc !== null).length
  return expected === 3 && group.items.length === 3 && new Set(group.items.map(item => item.result.id)).size === 3
}
function groupOverlays(groups: CandidateGroup[]): FaceOverlay[] {
  return groups.map(group => ({ id: group.id, bounds: group.bounds, landmarks: [], label: `Face ${group.number}`,
    labelParts: group.items.sort((left, right) => left.modelIndex - right.modelIndex).map(item => ({ text: `#${item.detection.ordinal} `, color: modelColors[item.modelIndex] })),
    isFace: groupIsApproved(group) ? true : groupIsRejected(group) ? false : null }))
}
function formatPercent(value: number | null) { return value === null ? '—' : `${Math.round(value * 1000) / 10}%` }

function DetectorMetrics({ result, groups, modelIndex, reviewed }: { result: ComparisonResult; groups: CandidateGroup[]; modelIndex: number; reviewed: boolean }) {
  const correct = result.detections.filter(face => face.isFace !== false).length, falsePositives = result.detections.filter(face => face.isFace === false).length
  const inferredMissed = groups.filter(group => groupIsApproved(group) && !group.items.some(item => item.result.id === result.id)).length
  const missed = (result.missedFaces ?? 0) + inferredMissed, complete = reviewed
  const precision = complete && correct + falsePositives > 0 ? correct / (correct + falsePositives) : null
  const recall = complete && correct + missed > 0 ? correct / (correct + missed) : null
  const f1 = precision === null || recall === null || precision + recall === 0 ? null : 2 * precision * recall / (precision + recall)
  return <tr><td><span className="comparison-model-dot" style={{ backgroundColor: modelColors[modelIndex] }} />{result.modelName}</td><td>{result.detections.length}</td><td>{correct}</td><td>{falsePositives}</td><td>{missed}</td><td>{formatPercent(f1)}</td></tr>
}
function DetectorSummary({ run, groups }: { run: ComparisonRun; groups: CandidateGroup[] }) {
  return <>
    <table className="comparison-metrics"><thead><tr><th>Model</th><th>Boxes</th><th>Faces</th><th>False</th><th>Missed</th><th>F1</th></tr></thead><tbody>{run.results.map((result, index) => <DetectorMetrics key={result.id} result={result} groups={groups} modelIndex={index} reviewed={run.reviewedAtUtc !== null} />)}</tbody></table>
    <details className="comparison-settings"><summary>Model settings and timing</summary>{run.results.map(result => <div key={result.id} className="comparison-model-settings"><strong>{result.modelName}</strong><span>{result.error ? result.error : `${result.elapsedMilliseconds?.toFixed(0)} ms · ${result.detections.length} boxes`}</span><pre>{JSON.stringify(result.configuration, null, 2)}</pre></div>)}</details>
  </>
}
function GroupCard({ group, asset, highlighted, onHover, onOpen, onReview, onDetectionReview, disabled }: { group: CandidateGroup; asset: AssetSummary | undefined; highlighted: boolean; onHover: (id: string | null) => void; onOpen: (target: PhotoPopupTarget) => void; onReview: (isFace: boolean) => void; onDetectionReview: (id: string, isFace: boolean) => void; disabled: boolean }) {
  const warnings = [...new Set(group.items.flatMap(item => item.detection.warnings))], state = groupState(group)
  return <article className={`comparison-group comparison-group-${state}${highlighted ? ' comparison-group-highlighted' : ''}`} onMouseEnter={() => onHover(group.id)} onMouseLeave={() => onHover(null)}>
    <button type="button" className="comparison-crop" aria-label={`Open candidate face ${group.number}`} onClick={() => onOpen({ title: `Candidate face ${group.number}`, assetId: asset?.id ?? '', overlays: groupOverlays([group]) })}><FaceCropPreview asset={asset} faceBounds={group.bounds} size={64} /></button>
    <div className="comparison-group-detail"><strong>Face {group.number}{state === 'rejected' ? ' · incorrect' : ''}</strong>
      <div className="comparison-model-chips">{[...group.items].sort((left, right) => left.modelIndex - right.modelIndex).map(item => <button key={item.detection.id} type="button" disabled={disabled} className={`comparison-model-chip${item.detection.isFace === false ? ' comparison-model-chip-rejected' : ''}`} style={{ backgroundColor: modelColors[item.modelIndex] }} title={item.detection.isFace === false ? 'Restore this detection' : 'Mark this detection as incorrect'} aria-label={`${modelLetters[item.modelIndex]} detection #${item.detection.ordinal}: ${item.detection.isFace === false ? 'restore' : 'mark incorrect'}`} onClick={() => onDetectionReview(item.detection.id, item.detection.isFace === false)}>{modelLetters[item.modelIndex]} #{item.detection.ordinal} · {item.detection.score.toFixed(2)}{item.detection.isFace === false ? ' ×' : ''}</button>)}</div>
      {warnings.length > 0 && <small className="comparison-group-warnings">{warnings.join(' · ')}</small>}
      <div className="comparison-label-actions"><button type="button" className="btn btn-xs" disabled={disabled} onClick={() => onReview(state === 'rejected')}>{state === 'rejected' ? 'Restore all' : 'Reject all'}</button></div>
    </div>
  </article>
}

function ComparisonContent({ assets }: { assets: AssetSummary[] }) {
  const review = useFaceComparison(), [popup, setPopup] = useState<PhotoPopupTarget | null>(null), [showAll, setShowAll] = useState(false), [hoveredGroupId, setHoveredGroupId] = useState<string | null>(null)
  const assetsById = new Map(assets.map(asset => [asset.id, asset])), run = review.run, asset = run ? assetsById.get(run.assetId) : undefined
  const groups = run ? candidateGroups(run) : []
  const workerFinished = run?.status === 'Done'
  const analysisSucceeded = workerFinished && run.results.every(result => result.error === null && result.completedAtUtc !== null)
  const canReview = analysisSucceeded
  const visibleGroups = showAll ? groups : groups.filter(group => !isAgreed(group, run!))
  const reviewGroup = (group: CandidateGroup, isFace: boolean) => run && review.reviewMany(run.id, group.items.map(item => ({ id: item.detection.id, isFace })))
  const highestMissed = run ? Math.max(0, ...run.results.map(result => result.missedFaces ?? 0)) : 0
  const modelDifferences = run ? groups.filter(group => !isAgreed(group, run)).length : 0
  const validationDone = analysisSucceeded && !run.isSkipped && run.reviewedAtUtc !== null
  const reviewStatus = !workerFinished ? 'Analyzing' : !analysisSucceeded ? 'Analysis failed' : run.isSkipped ? 'Skipped invalid photo' : validationDone ? 'Done' : 'Needs validation'
  return <div className="comparison-content">
    <p>Detections count as faces by default. Inspect the photo, mark incorrect boxes, then move to the next photo to finish its review. This experiment does not change person suggestions.</p>
    <div className="comparison-controls"><PhotoMultiSelect photos={assets.map(item => ({ id: item.id, name: item.originalFileName, storedFileName: item.storedFileName }))} selected={review.assetId ? [review.assetId] : []} onChange={ids => review.choosePhoto(ids.at(-1) ?? '')} addLabel={review.assetId ? 'Change photo…' : 'Choose a photo…'} searchPlaceholder="Search photos…" ariaLabel="Photo for detector comparison" /><button type="button" className="btn btn-primary" disabled={!review.assetId || review.busy || (review.pending && run?.assetId === review.assetId)} onClick={review.compare}>Compare all 3 models</button></div>
    {review.error && <p className="notes-error" role="alert">{review.error} <button type="button" className="btn btn-xs" onClick={review.reload}>Reload</button></p>}
    {review.history === null ? <p>Loading comparison history…</p> : <><div className="comparison-history"><Select value={review.runId} onChange={review.chooseRun} ariaLabel="Comparison history" placeholder="No comparisons yet" options={review.history.runs.map(item => ({ id: item.id, label: `${item.assetName} · ${new Date(item.createdAtUtc).toLocaleString()} · ${item.isSkipped ? 'Skipped invalid photo' : item.reviewedAtUtc ? 'Done' : 'Needs validation'}` }))} /><label className="comparison-show-reviewed"><input type="checkbox" checked={review.showReviewed} onChange={event => review.setShowReviewed(event.target.checked)} />Show reviewed</label>{review.page > 0 && <button type="button" className="btn btn-xs" onClick={() => review.changePage(review.page - 1)}>Newer</button>}{review.history.hasMore && <button type="button" className="btn btn-xs" onClick={() => review.changePage(review.page + 1)}>Older</button>}</div>{review.history.runs.length === 0 && !review.runId && <p>{review.showReviewed ? 'No comparisons yet. Choose a photo and run the three detectors.' : 'No photos need validation.'}</p>}</>}
    {review.runId && !run && <p>Loading results…</p>}
    {run && <><div className="comparison-run-head"><strong>{asset?.originalFileName ?? 'Photo'}</strong><span>{new Date(run.createdAtUtc).toLocaleString()} · <span className={validationDone ? 'comparison-status-done' : ''}>{reviewStatus}</span>{run.imageWidth ? ` · ${run.imageWidth} × ${run.imageHeight} px` : ''}</span></div>{run.error && <p className="notes-error">{run.error}</p>}
      {!workerFinished && <p role="status">Models are still running. This photo will appear here only after analysis completes.</p>}
      {workerFinished && !analysisSucceeded && <p className="notes-error">The comparison did not complete successfully, so this photo cannot be reviewed.</p>}
      {analysisSucceeded && <div className="comparison-stage"><FullPhotoPreview key={run.assetId} asset={asset} overlays={groupOverlays(groups)} highlightedOverlayId={hoveredGroupId ?? undefined} onOverlayHover={setHoveredGroupId} maxHeightVh={46} /><div className="comparison-photo-navigation"><button type="button" className="btn" aria-label="Previous photo" title="Previous photo" disabled={!review.hasPrevious || review.busy} onClick={() => review.navigate(-1)}>‹</button><button type="button" className="btn" aria-label="Next photo or finish review" title="Next photo or finish review" disabled={!review.hasNext || review.busy} onClick={() => review.navigate(1)}>›</button></div><div className="comparison-legend">{run.results.map((result, index) => <span key={result.id}><i style={{ backgroundColor: modelColors[index] }} />{modelLetters[index]} · {result.modelName}</span>)}</div><button type="button" className="btn btn-xs" onClick={() => setPopup({ title: 'Detector candidates', assetId: run.assetId, overlays: groupOverlays(groups) })}>Open full photo</button></div>}
      {canReview && <div className="comparison-actions"><button type="button" className="btn btn-xs" disabled={review.busy} onClick={() => review.skipped(run.id, !run.isSkipped)}>{run.isSkipped ? 'Restore this photo' : 'Skip invalid photo'}</button>{!run.isSkipped && <><button type="button" className="btn btn-xs" disabled={review.busy} onClick={() => review.missedForAll(run.id, highestMissed + 1)}>+1 face missed by all models</button>{highestMissed > 0 && <><span className="comparison-group-summary">{highestMissed} missed by all</span><button type="button" className="btn btn-xs" disabled={review.busy} onClick={() => review.missedForAll(run.id, highestMissed - 1)}>Undo one missed face</button></>}<button type="button" className="btn btn-xs" onClick={() => setShowAll(value => !value)}>{showAll ? 'Show only differences' : 'Show all groups'}</button><span className="comparison-group-summary">{groups.length} groups · {modelDifferences} model {modelDifferences === 1 ? 'difference' : 'differences'} · {validationDone ? 'reviewed' : 'inspect, then use →'}</span></>}</div>}
      {analysisSucceeded && groups.length === 0 && <p>No face candidates. If the photo contains a face, use “+1 face missed by all models”.</p>}
      {run.isSkipped ? <p className="comparison-note">This photo is excluded from detector metrics. Restore it to review its candidates.</p> : canReview && <div className="comparison-groups">{visibleGroups.map(group => <GroupCard key={group.id} group={group} asset={asset} highlighted={hoveredGroupId === group.id} onHover={setHoveredGroupId} onOpen={setPopup} onReview={isFace => reviewGroup(group, isFace)} onDetectionReview={(id, isFace) => review.review(id, isFace)} disabled={review.busy} />)}{visibleGroups.length === 0 && <p>All candidate groups were found by all three models. Use “Show all groups” to correct any of them.</p>}</div>}
      {!run.isSkipped && <><DetectorSummary run={run} groups={groups} /><p className="comparison-note">Click a coloured model number to mark only that detection incorrect or restore it. A face found by one or two models counts as missed by the others. F1 appears after the photo is reviewed.</p></>}</>}
    <PhotoPopupDialog target={popup} assetsById={assetsById} onClose={() => setPopup(null)} />
  </div>
}
export function FaceComparisonSection({ assets }: { assets: AssetSummary[] }) {
  const { collapsed, toggle } = useCollapsibleSection('photo-analysis:face-comparison')
  return <section className="subsection-panel photo-analysis-subsection face-comparison-section"><h3 className="tags-subhead"><button type="button" className="subsection-toggle" aria-expanded={!collapsed} onClick={toggle}><span className="section-toggle-caret" aria-hidden="true">▾</span>Face detector comparison</button></h3>{!collapsed && <ComparisonContent assets={assets} />}</section>
}
