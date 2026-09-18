import { useEffect, useRef, useState, type ReactNode } from 'react'
import type { ArchiveEvent, EventCandidate, FaceBounds, Location, Person, PersonReferenceFace, PhotoAnalysisCandidate, PhotoAnalysisCandidateMatch, SceneObservation } from '../../api/photoAnalysis'
import { createArchiveEvent, createLocation, createPerson } from '../../api/photoAnalysis'
import { fetchDownloadUrl } from '../../api/assets'
import type { AssetSummary } from '../../api/assets'
import { useCollapsibleSection } from '../../hooks/useCollapsibleSection'
import { Dropdown } from '../Dropdown/Dropdown'
import { InfoHint } from '../InfoHint/InfoHint'
import { MultiSelect } from '../MultiSelect/MultiSelect'
import { PhotoMultiSelect } from '../MultiSelect/PhotoMultiSelect'
import { ProgressiveImage } from '../ProgressiveImage/ProgressiveImage'
import { Select } from '../Select/Select'
import { usePhotoAnalysisSection } from './usePhotoAnalysisSection'
import './PhotoAnalysisSection.css'

// hasReferences tells the two empty-percent cases apart: a target the model could not compare
// against at all, and one it compared and left outside the stored top five.
interface ReviewTarget { id: string; name: string; hasReferences?: boolean }

/** The score is a ranking measure, not a probability — red up to 50%, blending to green at 100%. */
function scoreColor(score: number) {
  return `hsl(${Math.round(Math.min(1, Math.max(0, (score - 0.5) * 2)) * 120)} 75% 55%)`
}

function CandidateThumbnail({ asset }: { asset: AssetSummary | undefined }) {
  const [url, setUrl] = useState<string | null>(null)
  const [unavailable, setUnavailable] = useState(false)
  useEffect(() => {
    if (asset === undefined) return
    let cancelled = false
    void fetchDownloadUrl(asset.storedFileName).then(link => { if (!cancelled) setUrl(link) }).catch(() => { if (!cancelled) setUnavailable(true) })
    return () => { cancelled = true }
  }, [asset])
  if (url) {
    return <div className="candidate-thumbnail"><ProgressiveImage url={url} alt={`Photo ${asset?.originalFileName ?? ''}`} showPercent /></div>
  }
  const waiting = asset !== undefined && !unavailable
  return <div className="candidate-thumbnail">{waiting ? <ProgressiveImage url={null} alt="" showPercent /> : <span>Photo preview unavailable</span>}</div>
}

/** The full photo, optionally with a box drawn on one detected face — shared by the review preview and every Known * popup.
    The outer frame box letterboxes the photo: the inner box always carries the source aspect ratio,
    so the %-positioned face frame maps onto the visible image rather than the bars around it, and a
    square frame holds its shape instead of letting a portrait photo stretch the review row. */
function FullPhotoPreview({ asset, faceBounds, label, labelContent, square = false }: { asset: AssetSummary | undefined; faceBounds?: FaceBounds; label?: string; labelContent?: ReactNode; square?: boolean }) {
  const [url, setUrl] = useState<string | null>(null)
  const [unavailable, setUnavailable] = useState(false)
  const [sourceSize, setSourceSize] = useState<{ width: number; height: number } | null>(null)
  const [boxWidth, setBoxWidth] = useState<number | null>(null)
  const boxRef = useRef<HTMLDivElement>(null)
  const previewRef = useRef<HTMLDivElement>(null)
  const labelRef = useRef<HTMLSpanElement>(null)
  const [labelFit, setLabelFit] = useState<{ scale: number; clampPx: number } | null>(null)
  useEffect(() => {
    if (asset === undefined) return
    let cancelled = false
    void fetchDownloadUrl(asset.storedFileName).then(link => { if (!cancelled) setUrl(link) }).catch(() => { if (!cancelled) setUnavailable(true) })
    return () => { cancelled = true }
  }, [asset])

  // A non-square frame (the popup) has no CSS-definite height, so its fit is measured in pixels:
  // the width comes from the dialog, the height cap from the viewport.
  useEffect(() => {
    if (square) return
    const box = boxRef.current
    if (box === null) return
    const measure = () => setBoxWidth(box.clientWidth)
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(box)
    window.addEventListener('resize', measure)
    return () => { observer.disconnect(); window.removeEventListener('resize', measure) }
  }, [square, url])

  // The label hangs off the face box with white-space:nowrap, so a long name overflows the
  // photo: shrink it (floor at 72%, then ellipsis) to whatever fits between its edge and the
  // photo's right border.
  useEffect(() => {
    const preview = previewRef.current
    const label = labelRef.current
    if (url === null || preview === null || label === null) { setLabelFit(null); return }
    const fit = () => {
      const frame = label.offsetParent as HTMLElement | null
      const leftEdge = (frame?.offsetLeft ?? 0) + label.offsetLeft
      const available = Math.max(16, preview.clientWidth - leftEdge - 2)
      const scale = Math.min(1, Math.max(0.72, available / label.offsetWidth))
      setLabelFit({ scale, clampPx: available / scale })
    }
    fit()
    const observer = new ResizeObserver(fit)
    observer.observe(preview)
    return () => observer.disconnect()
  }, [url, label, sourceSize])

  if (url === null) {
    const waiting = asset !== undefined && !unavailable
    return <div className={`face-frame-box${square ? ' face-frame-box-square' : ''}`}>
      {waiting ? <ProgressiveImage url={null} alt="" showPercent /> : <span>Photo preview unavailable</span>}
    </div>
  }

  const topPercent = sourceSize === null || faceBounds === undefined ? 0 : Math.max(0, faceBounds.y / sourceSize.height * 100)
  const frameStyle = sourceSize === null || faceBounds === undefined ? undefined : {
    left: `${Math.max(0, faceBounds.x / sourceSize.width * 100)}%`,
    top: `${topPercent}%`,
    width: `${Math.min(100, faceBounds.width / sourceSize.width * 100)}%`,
    height: `${Math.min(100, faceBounds.height / sourceSize.height * 100)}%`,
  }
  // Not enough room above the box near the top edge of the photo — put the label under it instead.
  const labelBelow = topPercent < 15

  // Square: the frame box's height is CSS-definite (aspect-ratio:1), so percentages resolve exactly.
  let innerStyle: { width: string; height: string } | undefined
  if (sourceSize !== null) {
    if (square) {
      innerStyle = {
        width: `${Math.min(100, sourceSize.width / sourceSize.height * 100)}%`,
        height: `${Math.min(100, sourceSize.height / sourceSize.width * 100)}%`,
      }
    } else if (boxWidth !== null) {
      const width = Math.min(boxWidth, window.innerHeight * 0.68 * sourceSize.width / sourceSize.height)
      innerStyle = { width: `${width}px`, height: `${width * sourceSize.height / sourceSize.width}px` }
    }
  }

  return <div ref={boxRef} className={`face-frame-box${square ? ' face-frame-box-square' : ''}`}>
    <div ref={previewRef} className="face-frame-preview" style={innerStyle}>
      <ProgressiveImage
        url={url}
        alt={`${faceBounds ? 'Detected face in' : 'Photo'} ${asset?.originalFileName ?? 'photo'}`}
        showPercent
        onImageLoad={(image) => setSourceSize({ width: image.naturalWidth, height: image.naturalHeight })}
      />
      {frameStyle && <span className={`face-frame${labelBelow ? ' face-frame-label-below' : ''}`} style={frameStyle}>{label && <span ref={labelRef} title={label} style={{
        transform: `scale(${labelFit?.scale ?? 1})`,
        transformOrigin: labelBelow ? 'left top' : 'left bottom',
        maxWidth: labelFit ? `${labelFit.clampPx}px` : undefined,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
      }}>{labelContent ?? label}</span>}</span>}
    </div>
  </div>
}

function faceCrop(bounds: FaceBounds, source: { width: number; height: number }, size = 144) {
  const faceWidth = Math.min(bounds.width, source.width)
  const faceHeight = Math.min(bounds.height, source.height)
  const side = Math.min(Math.max(faceWidth, faceHeight) * 1.3, source.width, source.height)
  const centerX = Math.min(Math.max(bounds.x + bounds.width / 2, side / 2), source.width - side / 2)
  const centerY = Math.min(Math.max(bounds.y + bounds.height / 2, side / 2), source.height - side / 2)
  const scale = size / side
  return { width: source.width * scale, height: source.height * scale, left: -(centerX - side / 2) * scale, top: -(centerY - side / 2) * scale }
}

/** A tight square crop around one detected face, loaded from the full photo (no server-side thumbnail). */
function FaceCropPreview({ asset, faceBounds, size = 144 }: { asset: AssetSummary | undefined; faceBounds: FaceBounds; size?: number }) {
  const [url, setUrl] = useState<string | null>(null)
  const [unavailable, setUnavailable] = useState(false)
  const [sourceSize, setSourceSize] = useState<{ width: number; height: number } | null>(null)
  useEffect(() => {
    if (asset === undefined) return
    let cancelled = false
    void fetchDownloadUrl(asset.storedFileName).then(link => { if (!cancelled) setUrl(link) }).catch(() => { if (!cancelled) setUnavailable(true) })
    return () => { cancelled = true }
  }, [asset])

  const crop = url !== null && sourceSize !== null ? faceCrop(faceBounds, sourceSize, size) : null
  return <div className="face-crop-preview" style={{ width: size, height: size }}>
    {url === null && unavailable
      ? <span>Unavailable</span>
      : <ProgressiveImage
          url={url}
          alt=""
          ariaHidden
          imgStyle={crop ?? undefined}
          onImageLoad={(image) => { if (sourceSize === null) setSourceSize({ width: image.naturalWidth, height: image.naturalHeight }) }}
        />}
  </div>
}

function PersonCandidatePreview({ asset, faceBounds, label, labelContent, onOpen }: { asset: AssetSummary | undefined; faceBounds: FaceBounds; label?: string; labelContent?: ReactNode; onOpen?: () => void }) {
  const preview = <FullPhotoPreview asset={asset} faceBounds={faceBounds} label={label} labelContent={labelContent} square />
  return <div className="person-candidate-previews">
    <figure className="face-preview-panel"><figcaption>Original photo</figcaption>{onOpen === undefined ? preview : <button type="button" className="face-preview-open" aria-label="View full photo" onClick={onOpen}>{preview}</button>}</figure>
    <figure className="face-preview-panel"><figcaption>Face crop</figcaption><FaceCropPreview asset={asset} faceBounds={faceBounds} /></figure>
  </div>
}

/** One opened photo: whose record it belongs to, which photo, and what revoking it means. */
interface PhotoPopupTarget { title: string; assetId: string; faceBounds?: FaceBounds; score?: number; onRevoke?: () => void }

function KnownPersons({ people, assetsById, collapsed, onToggle, onRevoke }: { people: Person[]; assetsById: Map<string, AssetSummary>; collapsed: boolean; onToggle: () => void; onRevoke: (personId: string, faceOccurrenceId: string) => void }) {
  const [openPhoto, setOpenPhoto] = useState<PhotoPopupTarget | null>(null)
  const knownPeople = people.filter(person => person.referenceFaces.length > 0)
  return <>
    <PhotoAnalysisSubsection title="Known Persons" count={knownPeople.length} collapsed={collapsed} onToggle={onToggle}>
      {knownPeople.length === 0
        ? <p className="known-empty">No confirmed faces yet.</p>
        : <ul className="known-list">{knownPeople.map(person => <li key={person.id} className="known-row">
            <span className="known-name">{person.name}</span>
            <div className="known-person-faces">{person.referenceFaces.map((face: PersonReferenceFace) => <div key={face.id} className="known-face-item">
              <button type="button" className="known-face-thumb" aria-label={`View full photo of ${person.name}`} onClick={() => setOpenPhoto({ title: person.name, assetId: face.assetId, faceBounds: face.faceBounds, onRevoke: () => onRevoke(person.id, face.id) })}>
                <FaceCropPreview asset={assetsById.get(face.assetId)} faceBounds={face.faceBounds} size={64} />
              </button>
              <button type="button" className="btn btn-xs" onClick={() => onRevoke(person.id, face.id)}>Revoke</button>
            </div>)}</div>
          </li>)}</ul>}
    </PhotoAnalysisSubsection>
    <PhotoPopupDialog target={openPhoto} assetsById={assetsById} onClose={() => setOpenPhoto(null)} />
  </>
}

function PhotoPopupDialog({ target, assetsById, onClose }: { target: PhotoPopupTarget | null; assetsById: Map<string, AssetSummary>; onClose: () => void }) {
  const dialogRef = useRef<HTMLDialogElement>(null)
  useEffect(() => {
    const dialog = dialogRef.current
    if (dialog === null) return
    if (target !== null && !dialog.open) dialog.showModal()
    else if (target === null && dialog.open) dialog.close()
  }, [target])

  const asset = target === null ? undefined : assetsById.get(target.assetId)
  return <dialog ref={dialogRef} className="confirm-dialog face-popup-dialog" aria-labelledby="face-popup-title" onCancel={onClose} onClose={onClose}>
    {target !== null && <>
      <h3 id="face-popup-title">{target.title}{asset && <span className="face-popup-filename"> ({asset.originalFileName})</span>}{target.score !== undefined && <span className="face-popup-score">{confidenceBadge(target.score, target.score >= 0.5 ? 'Likely match' : 'Possible match')}</span>}</h3>
      <FullPhotoPreview asset={asset} faceBounds={target.faceBounds} />
      <div className="confirm-actions">{target.onRevoke !== undefined && <button type="button" className="btn btn-lg" onClick={() => { target.onRevoke?.(); onClose() }}>Revoke</button>}<button type="button" className="btn btn-lg" onClick={onClose}>Close</button></div>
    </>}
  </dialog>
}

/** A horizontal strip of full photos, each opening the popup and revocable in place. */
function KnownPhotoStrip({ photos, assetsById, recordName, onOpen, onRevoke }: { photos: { assetId: string }[]; assetsById: Map<string, AssetSummary>; recordName: string; onOpen: (assetId: string) => void; onRevoke: (assetId: string) => void }) {
  return <div className="known-photo-strip">{photos.map(photo => <div key={photo.assetId} className="known-photo-item">
    <button type="button" className="known-photo-thumb" aria-label={`View full photo of ${recordName}`} onClick={() => onOpen(photo.assetId)}>
      <CandidateThumbnail asset={assetsById.get(photo.assetId)} />
    </button>
    <button type="button" className="btn btn-xs" onClick={() => onRevoke(photo.assetId)}>Revoke</button>
  </div>)}</div>
}

function KnownLocations({ locations, assetsById, collapsed, onToggle, onRevoke }: { locations: Location[]; assetsById: Map<string, AssetSummary>; collapsed: boolean; onToggle: () => void; onRevoke: (locationId: string, assetId: string) => void }) {
  const [openPhoto, setOpenPhoto] = useState<PhotoPopupTarget | null>(null)
  const knownLocations = locations.filter(location => location.referencePhotos.length > 0)
  return <>
    <PhotoAnalysisSubsection title="Known Locations" count={knownLocations.length} collapsed={collapsed} onToggle={onToggle}>
      {knownLocations.length === 0
        ? <p className="known-empty">No confirmed location photos yet.</p>
        : <ul className="known-list">{knownLocations.map(location => <li key={location.id} className="known-row">
            <span className="known-name">{location.name}</span>
            <small>{location.kind.toLowerCase()} · {location.referencePhotos.length} confirmed photos</small>
            <KnownPhotoStrip
              photos={location.referencePhotos}
              assetsById={assetsById}
              recordName={location.name}
              onOpen={assetId => setOpenPhoto({ title: location.name, assetId, onRevoke: () => onRevoke(location.id, assetId) })}
              onRevoke={assetId => onRevoke(location.id, assetId)}
            />
          </li>)}</ul>}
    </PhotoAnalysisSubsection>
    <PhotoPopupDialog target={openPhoto} assetsById={assetsById} onClose={() => setOpenPhoto(null)} />
  </>
}

function KnownEvents({ events, assetsById, collapsed, onToggle, onRevoke }: { events: ArchiveEvent[]; assetsById: Map<string, AssetSummary>; collapsed: boolean; onToggle: () => void; onRevoke: (eventId: string, assetId: string) => void }) {
  const [openPhoto, setOpenPhoto] = useState<PhotoPopupTarget | null>(null)
  return <>
    <PhotoAnalysisSubsection title="Known Events" count={events.length} collapsed={collapsed} onToggle={onToggle}>
      {events.length === 0
        ? <p className="known-empty">No events yet.</p>
        : <ul className="known-list">{events.map(event => <li key={event.id} className="known-row">
            <span className="known-name">{event.title}</span>
            <small>{event.occurredOn ?? 'No date'} · {event.locationName ?? 'No location'} · {event.assetIds.length} photos</small>
            {event.assetIds.length > 0 && <KnownPhotoStrip
              photos={event.assetIds.map(assetId => ({ assetId }))}
              assetsById={assetsById}
              recordName={event.title}
              onOpen={assetId => setOpenPhoto({ title: event.title, assetId, onRevoke: () => onRevoke(event.id, assetId) })}
              onRevoke={assetId => onRevoke(event.id, assetId)}
            />}
          </li>)}</ul>}
    </PhotoAnalysisSubsection>
    <PhotoPopupDialog target={openPhoto} assetsById={assetsById} onClose={() => setOpenPhoto(null)} />
  </>
}

type CandidateKind = 'Person' | 'Location' | 'Event'

/** Below this the proposal is treated as worthless: "create a new record" becomes the primary answer
    instead of confirming a guess the model itself does not trust. */
const CONFIDENT_SCORE = 0.3

const KIND_WORDING: Record<CandidateKind, { other: string; create: string; createPlaceholder: string; noReferences: string | null; noMatch: string }> = {
  Person: { other: "It's someone else…", create: 'Add face for new person', createPlaceholder: 'New person name', noReferences: 'no reference faces', noMatch: 'No confident match' },
  Location: { other: "It's a different place…", create: 'Create location & add photo', createPlaceholder: 'New location name', noReferences: 'no reference photos', noMatch: 'No confident match' },
  Event: { other: "It's a different event…", create: 'Create event & add photo', createPlaceholder: 'New event title', noReferences: null, noMatch: 'No confident match' },
}

/** One formatting path for every confidence badge — the percent is the same candidate.score the Model evidence shows, never a separately picked number. */
function confidenceBadge(score: number, word: string) {
  return <span className="confidence-badge" style={{ color: scoreColor(score) }}>{word} ({Math.round(score * 100)}%)</span>
}

/** A native select cannot colour its own options, and the match percent has to carry the section's score colours. */
function ExistingTargetPicker({ label, ariaLabel, targets, matches, noReferencesLabel, onChoose }: {
  label: string
  ariaLabel: string
  targets: ReviewTarget[]
  matches: PhotoAnalysisCandidateMatch[]
  noReferencesLabel: string | null
  onChoose: (targetId: string) => void
}) {
  const scoreByTarget = new Map(matches.map(match => [match.targetId, match.score]))
  const ranked = [...targets].sort((left, right) => {
    const leftScore = scoreByTarget.get(left.id)
    const rightScore = scoreByTarget.get(right.id)
    if (leftScore !== undefined && rightScore !== undefined) return rightScore - leftScore
    if (leftScore !== undefined) return -1
    if (rightScore !== undefined) return 1
    return left.name.localeCompare(right.name)
  })
  return <Dropdown align="start" panelClassName="target-picker-panel" trigger={({ toggle, isOpen }) =>
    <button type="button" className="field field-xs target-picker-trigger" aria-label={ariaLabel} aria-expanded={isOpen} onClick={toggle}>{label}</button>}>
    {close => <div className="target-picker-options">
      {ranked.map(target => {
        const score = scoreByTarget.get(target.id)
        return <button type="button" key={target.id} className="target-picker-option" onClick={() => { close(); onChoose(target.id) }}>
          <span className="target-picker-name">{target.name}</span>
          {score !== undefined
            ? <span style={{ color: scoreColor(score) }}>{Math.round(score * 100)}%</span>
            : target.hasReferences === false
              ? noReferencesLabel !== null && <span className="target-picker-note">{noReferencesLabel}</span>
              : target.hasReferences === true && <span className="target-picker-note">score too low</span>}
        </button>
      })}
      {ranked.length === 0 && <span className="target-picker-empty">Nothing to choose from yet</span>}
    </div>}
  </Dropdown>
}

function CandidateReviews({ kind, candidates, targets, onReview, onReviewAsNew, assetsById, showPhoto = false }: {
  kind: CandidateKind
  candidates: PhotoAnalysisCandidate[]
  targets: ReviewTarget[]
  onReview: (candidateId: string, decision: string, chosenTargetId: string | null) => void
  onReviewAsNew: (candidateId: string, createTarget: () => Promise<string>) => void
  assetsById: Map<string, AssetSummary>
  showPhoto?: boolean
}) {
  const [newNames, setNewNames] = useState<Record<string, string>>({})
  const [newLocationKinds, setNewLocationKinds] = useState<Record<string, string>>({})
  const [openPhoto, setOpenPhoto] = useState<PhotoPopupTarget | null>(null)
  if (candidates.length === 0) return null
  const wording = KIND_WORDING[kind]
  return <div className="candidate-reviews"><h4>To review <span className="section-count">{candidates.length}</span></h4>{candidates.map(candidate => {
    const faceBounds = showPhoto && candidate.kind === 'Person' ? candidate.faceBounds : null
    const proposedName = candidate.proposedTargetName ?? candidate.proposedLabel
    const confident = candidate.score >= CONFIDENT_SCORE && proposedName !== null
    const newName = newNames[candidate.id] ?? ''
    const createNew = (): Promise<string> => {
      if (kind === 'Person') return createPerson(newName.trim())
      if (kind === 'Location') return createLocation(newName.trim(), newLocationKinds[candidate.id] ?? 'Physical')
      return createArchiveEvent({ title: newName.trim(), occurredOn: null, locationId: null, personIds: [], assetIds: [candidate.subjectAssetId] })
    }
    const chooseExisting = (value: string) => { if (value) onReview(candidate.id, 'Corrected', value) }
    return <article className={faceBounds != null ? 'candidate-review candidate-review-with-face-preview' : showPhoto ? 'candidate-review candidate-review-with-image' : 'candidate-review'} key={candidate.id}>
    {showPhoto && (faceBounds != null ? <PersonCandidatePreview key={candidate.subjectFaceOccurrenceId} asset={assetsById.get(candidate.subjectAssetId)} faceBounds={faceBounds}
      label={`${Math.round(candidate.score * 100)}% · ${proposedName ?? 'Unknown'}`}
      labelContent={<><span style={{ color: scoreColor(candidate.score) }}>{Math.round(candidate.score * 100)}%</span> · {proposedName ?? 'Unknown'}</>}
      onOpen={() => setOpenPhoto({ title: proposedName ?? 'Original photo', assetId: candidate.subjectAssetId, faceBounds, score: candidate.score })} /> : <CandidateThumbnail key={candidate.subjectAssetId} asset={assetsById.get(candidate.subjectAssetId)} />)}
    <div className="candidate-review-details">
      <span className="candidate-subject">Photo: {candidate.subjectAssetName}</span>
      {confident
        ? <p className="candidate-question">Is this «{proposedName}»? {confidenceBadge(candidate.score, candidate.score >= 0.5 ? 'Likely match' : 'Possible match')}</p>
        : <p className="candidate-question">{wording.noMatch}</p>}
      <div className="candidate-actions">
        {confident && candidate.proposedTargetId && <button className="btn btn-xs btn-primary" type="button" onClick={() => onReview(candidate.id, 'Accepted', null)}>Yes, it's «{proposedName}»</button>}
        {!confident && <>
          {kind === 'Location' && <Select ariaLabel="New location kind" value={newLocationKinds[candidate.id] ?? 'Physical'} onChange={value => setNewLocationKinds(current => ({ ...current, [candidate.id]: value }))} options={[{ id: 'Physical', label: 'Physical' }, { id: 'Visual', label: 'Visual' }]} />}
          <input className="field field-xs" placeholder={wording.createPlaceholder} value={newName} onChange={event => setNewNames(current => ({ ...current, [candidate.id]: event.target.value }))} />
          <button className="btn btn-xs btn-primary" type="button" disabled={!newName.trim()} onClick={() => onReviewAsNew(candidate.id, createNew)}>{wording.create}</button>
          <span className="action-or">or</span>
          <ExistingTargetPicker label="Choose existing…" ariaLabel={wording.other} targets={targets} matches={candidate.matches} noReferencesLabel={wording.noReferences} onChoose={chooseExisting} />
        </>}
        {confident && <ExistingTargetPicker label={wording.other} ariaLabel={wording.other} targets={targets} matches={candidate.matches} noReferencesLabel={wording.noReferences} onChoose={chooseExisting} />}
        <button className="btn btn-xs candidate-reject" type="button" onClick={() => onReview(candidate.id, 'Rejected', null)}>Reject</button>
      </div>
      <details><summary>Model evidence</summary><p className="candidate-evidence-meta">score {candidate.score.toFixed(3)} · rank #{candidate.rank}</p><pre>{candidate.signalsJson}</pre></details>
    </div>
  </article>})}<PhotoPopupDialog target={openPhoto} assetsById={assetsById} onClose={() => setOpenPhoto(null)} /></div>
}

function PhotoAnalysisSubsection({ title, count, collapsed, onToggle, children }: { title: string; count: number; collapsed: boolean; onToggle: () => void; children: ReactNode }) {
  return <section className="subsection-panel photo-analysis-subsection"><h3 className="tags-subhead"><button type="button" className="subsection-toggle" aria-expanded={!collapsed} onClick={onToggle}><span className="section-toggle-caret" aria-hidden="true">▾</span>{title}<span className="section-count">{count}</span></button></h3>{!collapsed && children}</section>
}

/** Word for a 0–1 observation confidence — the raw decimal is never shown; the badge shares the candidate score's colour scale. */
function confidenceWord(confidence: number) {
  return confidence >= 0.8 ? 'High' : confidence >= 0.5 ? 'Medium' : 'Low'
}

function SceneObservationReviews({ observations, assetsById, onReview }: { observations: SceneObservation[]; assetsById: Map<string, AssetSummary>; onReview: (observationId: string, kind: string) => void }) {
  if (observations.length === 0) return null
  const byAsset = new Map<string, SceneObservation[]>()
  for (const observation of observations) {
    byAsset.set(observation.assetId, [...byAsset.get(observation.assetId) ?? [], observation])
  }
  return <div className="candidate-reviews"><h4>Scene observations <span className="section-count">{observations.length}</span></h4>{[...byAsset.entries()].map(([assetId, grouped]) => <article className="observation-review" key={assetId}>
    <CandidateThumbnail asset={assetsById.get(assetId)} />
    <div className="observation-details">
      <span className="observation-photo">{grouped[0].assetName}</span>
      {grouped.map(observation => <div className="observation-item" key={observation.id}>
        <span className="observation-meta"><span className="observation-kind">{observation.kind}</span>{confidenceBadge(observation.confidence, confidenceWord(observation.confidence))}</span>
        <p className="observation-description">{observation.description}</p>
        {(() => { const people = [observation.subjectPersonName, observation.relatedPersonName].filter(Boolean).join(' → '); return people !== '' && <span className="observation-people">{people}</span> })()}
        <details><summary>Visible evidence</summary><p>{observation.evidence}</p></details>
        <div className="candidate-actions"><button className="btn btn-xs" type="button" onClick={() => onReview(observation.id, 'Confirmed')}>Confirm</button><button className="btn btn-xs candidate-reject" type="button" onClick={() => onReview(observation.id, 'Rejected')}>Reject</button></div>
      </div>)}
    </div>
  </article>)}</div>
}

function EventCandidateReviews({ candidates, events, locations, people, assetsById, onReview }: { candidates: EventCandidate[]; events: ReviewTarget[]; locations: Location[]; people: Person[]; assetsById: Map<string, AssetSummary>; onReview: (candidateId: string, body: { kind: string; chosenEventId: string | null; title: string | null; occurredOn: string | null; locationId: string | null; personIds: string[]; assetIds: string[] }) => void }) {
  const [selectedPhotos, setSelectedPhotos] = useState<Record<string, string[]>>({})
  const [titles, setTitles] = useState<Record<string, string>>({})
  const [dates, setDates] = useState<Record<string, string>>({})
  const [locationsByCandidate, setLocationsByCandidate] = useState<Record<string, string>>({})
  const [peopleByCandidate, setPeopleByCandidate] = useState<Record<string, string[]>>({})
  const [existingEvents, setExistingEvents] = useState<Record<string, string>>({})
  if (candidates.length === 0) return null
  return <div className="candidate-reviews"><h4>Possible events <span className="section-count">{candidates.length}</span></h4>{candidates.map(candidate => {
    const photos = selectedPhotos[candidate.id] ?? candidate.photos.map(photo => photo.id)
    const candidatePeople = peopleByCandidate[candidate.id] ?? []
    return <article className="candidate-review" key={candidate.id}>
      <strong>{candidate.photos.length} photos · score {candidate.score.toFixed(3)}</strong>
      <span>{candidate.photos.map(photo => photo.name).join(', ')}</span>
      <details><summary>Clustering evidence</summary><pre>{candidate.signalsJson}</pre></details>
      <PhotoMultiSelect
        ariaLabel={`Include photos of ${candidate.photos.length}-photo candidate`}
        addLabel="Add photos…"
        searchPlaceholder="Search photos…"
        photos={candidate.photos.map(photo => ({ id: photo.id, name: photo.name, storedFileName: assetsById.get(photo.id)?.storedFileName }))}
        selected={photos}
        onChange={next => setSelectedPhotos(current => ({ ...current, [candidate.id]: next }))}
      />
      <input className="field field-xs" value={titles[candidate.id] ?? ''} onChange={event => setTitles(current => ({ ...current, [candidate.id]: event.target.value }))} placeholder="New event title" aria-label="New event title" />
      <input className="field field-xs" type="date" value={dates[candidate.id] ?? candidate.suggestedOccurredOn ?? ''} onChange={event => setDates(current => ({ ...current, [candidate.id]: event.target.value }))} aria-label="Event date" />
      <Select ariaLabel="Event location" value={locationsByCandidate[candidate.id] ?? ''} onChange={value => setLocationsByCandidate(current => ({ ...current, [candidate.id]: value }))} placeholder="No location" options={locations.map(location => ({ id: location.id, label: location.name }))} />
      <MultiSelect
        ariaLabel="Event people"
        placeholder={candidatePeople.length === 0 ? 'Add people…' : 'Add more people…'}
        options={people.map(person => ({ id: person.id, label: person.name }))}
        selected={candidatePeople}
        onChange={next => setPeopleByCandidate(current => ({ ...current, [candidate.id]: next }))}
      />
      <div className="candidate-actions"><button className="btn btn-xs" type="button" disabled={!titles[candidate.id]?.trim() || photos.length === 0} onClick={() => onReview(candidate.id, { kind: 'Created', chosenEventId: null, title: titles[candidate.id], occurredOn: dates[candidate.id] ?? candidate.suggestedOccurredOn ?? null, locationId: locationsByCandidate[candidate.id] || null, personIds: candidatePeople, assetIds: photos })}>Create event</button><Select ariaLabel="Existing event" value={existingEvents[candidate.id] ?? ''} onChange={value => setExistingEvents(current => ({ ...current, [candidate.id]: value }))} placeholder="Add to existing…" options={events.map(event => ({ id: event.id, label: event.name }))} /><button className="btn btn-xs" type="button" disabled={!existingEvents[candidate.id] || photos.length === 0} onClick={() => onReview(candidate.id, { kind: 'Attached', chosenEventId: existingEvents[candidate.id], title: null, occurredOn: null, locationId: null, personIds: [], assetIds: photos })}>Add selected photos</button><button className="btn btn-xs candidate-reject" type="button" onClick={() => onReview(candidate.id, { kind: 'Rejected', chosenEventId: null, title: null, occurredOn: null, locationId: null, personIds: [], assetIds: [] })}>Reject</button></div>
    </article>
  })}</div>
}

export function PhotoAnalysisSection() {
  const { data, assets, error, addPerson, addLocation, addEvent, reviewCandidate, reviewCandidateAsNew, reviewSceneObservation, reviewEventCandidate, queueFaceAnalysis, queueSceneAnalysis, queueSceneObservations, queueEventAnalysis, revokeReferenceFace, revokeLocationPhoto, detachEventPhoto } = usePhotoAnalysisSection()
  const { collapsed: peopleCollapsed, toggle: togglePeople } = useCollapsibleSection('photo-analysis:people')
  const { collapsed: knownPersonsCollapsed, toggle: toggleKnownPersons } = useCollapsibleSection('photo-analysis:known-persons')
  const { collapsed: knownLocationsCollapsed, toggle: toggleKnownLocations } = useCollapsibleSection('photo-analysis:known-locations')
  const { collapsed: knownEventsCollapsed, toggle: toggleKnownEvents } = useCollapsibleSection('photo-analysis:known-events')
  const { collapsed: locationsCollapsed, toggle: toggleLocations } = useCollapsibleSection('photo-analysis:locations')
  const { collapsed: eventsCollapsed, toggle: toggleEvents } = useCollapsibleSection('photo-analysis:events')
  const [personName, setPersonName] = useState('')
  const [locationName, setLocationName] = useState('')
  const [locationKind, setLocationKind] = useState('Physical')
  const [eventTitle, setEventTitle] = useState('')
  const [eventDate, setEventDate] = useState('')
  const [eventLocation, setEventLocation] = useState('')
  const [personIds, setPersonIds] = useState<string[]>([])
  const [assetIds, setAssetIds] = useState<string[]>([])

  if (data === null) return <section className="photo-analysis"><h2>Photo analysis</h2><p>Loading archive data…</p></section>
  const total = data.people.length + data.locations.length + data.events.length
  const personCandidates = data.pendingCandidates.filter(candidate => candidate.kind === 'Person')
  const locationCandidates = data.pendingCandidates.filter(candidate => candidate.kind === 'Location')
  const eventCandidates = data.pendingCandidates.filter(candidate => candidate.kind === 'Event')
  const assetsById = new Map(assets.map(asset => [asset.id, asset]))
  return <section className="photo-analysis">
    <h2><span>Photo analysis</span><span className="section-count">{total}</span></h2>
    {error && <p className="notes-error">{error}</p>}
    <div className="photo-analysis-sections">
      <PhotoAnalysisSubsection title="Persons" count={data.people.length} collapsed={peopleCollapsed} onToggle={togglePeople}><form onSubmit={(event) => { event.preventDefault(); if (personName.trim()) { addPerson(personName); setPersonName('') } }}><input className="field field-xs" value={personName} onChange={event => setPersonName(event.target.value)} placeholder="Name" /><button className="btn btn-xs">Add</button></form><div className="face-analysis-action"><button className="btn btn-xs" type="button" onClick={queueFaceAnalysis}>Analyze unprocessed photos</button><InfoHint id="face-analysis" label="What this button does" description="Fingerprints new photos, then queues face analysis for photos never scanned before. Photos already scanned are skipped — click again after fingerprinting finishes to scan them." /><small>{data.faceAnalysisStatus.imagesWithoutFingerprint > 0 ? `${data.faceAnalysisStatus.imagesWithoutFingerprint} images need fingerprinting` : `${data.faceAnalysisStatus.pendingFaceJobs} face scans pending`}</small></div><CandidateReviews kind="Person" candidates={personCandidates} targets={data.people.map(person => ({ id: person.id, name: person.name, hasReferences: person.referenceFaces.length > 0 }))} onReview={reviewCandidate} onReviewAsNew={reviewCandidateAsNew} assetsById={assetsById} showPhoto /><KnownPersons people={data.people} assetsById={assetsById} collapsed={knownPersonsCollapsed} onToggle={toggleKnownPersons} onRevoke={revokeReferenceFace} /></PhotoAnalysisSubsection>
      <PhotoAnalysisSubsection title="Locations" count={data.locations.length} collapsed={locationsCollapsed} onToggle={toggleLocations}><form onSubmit={(event) => { event.preventDefault(); if (locationName.trim()) { addLocation(locationName, locationKind); setLocationName('') } }}><input className="field field-xs" value={locationName} onChange={event => setLocationName(event.target.value)} placeholder="Location" /><Select ariaLabel="Location kind" value={locationKind} onChange={setLocationKind} options={[{ id: 'Physical', label: 'Physical' }, { id: 'Visual', label: 'Visual' }]} /><button className="btn btn-xs">Add</button></form><div className="face-analysis-action"><button className="btn btn-xs" type="button" onClick={queueSceneAnalysis}>Refresh location suggestions</button><small>{data.sceneAnalysisStatus.pendingSceneJobs} scene scans pending</small></div><CandidateReviews kind="Location" candidates={locationCandidates} targets={data.locations.map(location => ({ id: location.id, name: location.name, hasReferences: location.confirmedPhotoCount > 0 }))} onReview={reviewCandidate} onReviewAsNew={reviewCandidateAsNew} assetsById={assetsById} showPhoto /><KnownLocations locations={data.locations} assetsById={assetsById} collapsed={knownLocationsCollapsed} onToggle={toggleKnownLocations} onRevoke={revokeLocationPhoto} /></PhotoAnalysisSubsection>
      <PhotoAnalysisSubsection title="Events" count={data.events.length} collapsed={eventsCollapsed} onToggle={toggleEvents}><form className="event-form" onSubmit={(event) => { event.preventDefault(); if (eventTitle.trim()) { addEvent(eventTitle, eventDate || null, eventLocation || null, personIds, assetIds); setEventTitle(''); setEventDate(''); setEventLocation(''); setPersonIds([]); setAssetIds([]) } }}><div className="event-form-row"><input className="field field-xs" value={eventTitle} onChange={event => setEventTitle(event.target.value)} placeholder="Event title" aria-label="Event title" /><input className="field field-xs" type="date" value={eventDate} onChange={event => setEventDate(event.target.value)} aria-label="Event date" /><Select ariaLabel="Event location" value={eventLocation} onChange={setEventLocation} placeholder="No location yet" options={data.locations.map(location => ({ id: location.id, label: location.name }))} /></div><MultiSelect ariaLabel="Event people" placeholder={personIds.length === 0 ? 'Add people…' : 'Add more people…'} options={data.people.map(person => ({ id: person.id, label: person.name }))} selected={personIds} onChange={setPersonIds} /><PhotoMultiSelect ariaLabel="Event photos" addLabel={assetIds.length === 0 ? 'Add photos…' : 'Add more photos…'} searchPlaceholder="Search photos…" photos={assets.map(asset => ({ id: asset.id, name: asset.originalFileName, storedFileName: asset.storedFileName }))} selected={assetIds} onChange={setAssetIds} /><div className="event-form-footer"><button className="btn btn-xs">Create event</button></div></form><div className="face-analysis-action"><button className="btn btn-xs" type="button" onClick={queueEventAnalysis}>Refresh event candidates</button><small>{data.eventAnalysisStatus.pendingEventJobs} event scans pending</small></div><EventCandidateReviews candidates={data.pendingEventCandidates} events={data.events.map(item => ({ id: item.id, name: item.title }))} locations={data.locations} people={data.people} assetsById={assetsById} onReview={reviewEventCandidate} /><div className="face-analysis-action"><button className="btn btn-xs" type="button" onClick={queueSceneObservations}>Refresh scene observations</button><small>{data.observationAnalysisStatus.pendingObservationJobs} observation scans pending</small></div><SceneObservationReviews observations={data.pendingSceneObservations} assetsById={assetsById} onReview={reviewSceneObservation} /><CandidateReviews kind="Event" candidates={eventCandidates} targets={data.events.map(item => ({ id: item.id, name: item.title }))} onReview={reviewCandidate} onReviewAsNew={reviewCandidateAsNew} assetsById={assetsById} /><KnownEvents events={data.events} assetsById={assetsById} collapsed={knownEventsCollapsed} onToggle={toggleKnownEvents} onRevoke={detachEventPhoto} /></PhotoAnalysisSubsection>
    </div>
  </section>
}
