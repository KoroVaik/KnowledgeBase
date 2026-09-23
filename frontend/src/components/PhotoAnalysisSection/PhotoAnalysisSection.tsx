import { useState, type ReactNode } from 'react'
import { FaceComparisonSection } from '../FaceComparisonSection/FaceComparisonSection'
import { FaceRecognitionComparisonSection } from '../FaceRecognitionComparisonSection/FaceRecognitionComparisonSection'
import type { ArchiveEvent, EventCandidate, Location, Person, PersonReferenceFace, PhotoAnalysisCandidate, PhotoAnalysisCandidateMatch, SceneObservation } from '../../api/photoAnalysis'
import { createArchiveEvent, createLocation } from '../../api/photoAnalysis'
import { useAssetPreview } from '../../hooks/useAssetPreview'
import type { AssetSummary } from '../../api/assets'
import { useCollapsibleSection } from '../../hooks/useCollapsibleSection'
import { Dropdown } from '../Dropdown/Dropdown'
import { FaceCropPreview, PhotoPopupDialog, type PhotoPopupTarget } from '../FacePreview/FacePreview'
import { InfoHint } from '../InfoHint/InfoHint'
import { MultiSelect } from '../MultiSelect/MultiSelect'
import { PhotoMultiSelect } from '../MultiSelect/PhotoMultiSelect'
import { PeopleReviewSection } from '../PeopleReviewSection/PeopleReviewSection'
import { ProgressiveImage } from '../ProgressiveImage/ProgressiveImage'
import { Select } from '../Select/Select'
import { usePhotoAnalysisSection } from './usePhotoAnalysisSection'
import './PhotoAnalysisSection.css'

// hasReferences tells the two empty-percent cases apart: a target the model could not compare
// against at all, and one it compared and left outside the stored top five.
interface ReviewTarget { id: string; name: string; hasReferences?: boolean }

/** The calibrated confidence is a 0-1 probability — red at 50% (uncertain), blending to green at 100%. */
function scoreColor(score: number) {
  return `hsl(${Math.round(Math.min(1, Math.max(0, (score - 0.5) * 2)) * 120)} 75% 55%)`
}

function CandidateThumbnail({ asset }: { asset: AssetSummary | undefined }) {
  const { url, unavailable } = useAssetPreview(asset?.storedFileName, asset?.id, 'PhotoThumbnail')
  if (url) {
    return <div className="candidate-thumbnail"><ProgressiveImage url={url} alt={`Photo ${asset?.originalFileName ?? ''}`} showPercent /></div>
  }
  const waiting = asset !== undefined && !unavailable
  return <div className="candidate-thumbnail">{waiting ? <ProgressiveImage url={null} alt="" showPercent /> : <span>Photo preview unavailable</span>}</div>
}

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

type CandidateKind = 'Location' | 'Event'

/** Below this the proposal is treated as worthless: "create a new record" becomes the primary answer
    instead of confirming a guess the model itself does not trust. */
const CONFIDENT_SCORE = 0.3

const KIND_WORDING: Record<CandidateKind, { other: string; create: string; createPlaceholder: string; noReferences: string | null; noMatch: string }> = {
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
  const confidenceByTarget = new Map(matches.map(match => [match.targetId, match.confidence]))
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
            ? <span style={{ color: scoreColor(confidenceByTarget.get(target.id) ?? score) }}>{Math.round((confidenceByTarget.get(target.id) ?? score) * 100)}%</span>
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
  if (candidates.length === 0) return null
  const wording = KIND_WORDING[kind]
  return <div className="candidate-reviews"><h4>To review <span className="section-count">{candidates.length}</span></h4>{candidates.map(candidate => {
    const proposedName = candidate.proposedTargetName ?? candidate.proposedLabel
    const confident = candidate.score >= CONFIDENT_SCORE && proposedName !== null
    const newName = newNames[candidate.id] ?? ''
    const createNew = (): Promise<string> => {
      if (kind === 'Location') return createLocation(newName.trim(), newLocationKinds[candidate.id] ?? 'Physical')
      return createArchiveEvent({ title: newName.trim(), occurredOn: null, locationId: null, personIds: [], assetIds: [candidate.subjectAssetId] })
    }
    const chooseExisting = (value: string) => { if (value) onReview(candidate.id, 'Corrected', value) }
    return <article className={showPhoto ? 'candidate-review candidate-review-with-image' : 'candidate-review'} key={candidate.id}>
    {showPhoto && <CandidateThumbnail key={candidate.subjectAssetId} asset={assetsById.get(candidate.subjectAssetId)} />}
    <div className="candidate-review-details">
      <span className="candidate-subject">Photo: {candidate.subjectAssetName}</span>
      {confident
        ? <p className="candidate-question">Is this «{proposedName}»? {confidenceBadge(candidate.confidence, candidate.confidence >= 0.5 ? 'Likely match' : 'Possible match')}</p>
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
  </article>})}</div>
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
  const locationCandidates = data.pendingCandidates.filter(candidate => candidate.kind === 'Location')
  const eventCandidates = data.pendingCandidates.filter(candidate => candidate.kind === 'Event')
  const assetsById = new Map(assets.map(asset => [asset.id, asset]))
  return <section className="photo-analysis">
    <h2><span>Photo analysis</span><span className="section-count">{total}</span></h2>
    {error && <p className="notes-error">{error}</p>}
    <div className="photo-analysis-sections">
      <FaceComparisonSection assets={assets} />
      <FaceRecognitionComparisonSection assets={assets} />
      <PhotoAnalysisSubsection title="Persons" count={data.people.length} collapsed={peopleCollapsed} onToggle={togglePeople}><form onSubmit={(event) => { event.preventDefault(); if (personName.trim()) { addPerson(personName); setPersonName('') } }}><input className="field field-xs" value={personName} onChange={event => setPersonName(event.target.value)} placeholder="Name" /><button className="btn btn-xs">Add</button></form><div className="face-analysis-action"><button className="btn btn-xs" type="button" onClick={queueFaceAnalysis}>Analyze unprocessed photos</button><InfoHint id="face-analysis" label="What this button does" description="Fingerprints new photos, then queues face analysis for photos never scanned before. Photos already scanned are skipped — click again after fingerprinting finishes to scan them." /><small>{data.faceAnalysisStatus.imagesWithoutFingerprint > 0 ? `${data.faceAnalysisStatus.imagesWithoutFingerprint} images need fingerprinting` : `${data.faceAnalysisStatus.pendingFaceJobs} face scans pending`}</small></div><PeopleReviewSection people={data.people} assetsById={assetsById} /><KnownPersons people={data.people} assetsById={assetsById} collapsed={knownPersonsCollapsed} onToggle={toggleKnownPersons} onRevoke={revokeReferenceFace} /></PhotoAnalysisSubsection>
      <PhotoAnalysisSubsection title="Locations" count={data.locations.length} collapsed={locationsCollapsed} onToggle={toggleLocations}><form onSubmit={(event) => { event.preventDefault(); if (locationName.trim()) { addLocation(locationName, locationKind); setLocationName('') } }}><input className="field field-xs" value={locationName} onChange={event => setLocationName(event.target.value)} placeholder="Location" /><Select ariaLabel="Location kind" value={locationKind} onChange={setLocationKind} options={[{ id: 'Physical', label: 'Physical' }, { id: 'Visual', label: 'Visual' }]} /><button className="btn btn-xs">Add</button></form><div className="face-analysis-action"><button className="btn btn-xs" type="button" onClick={queueSceneAnalysis}>Refresh location suggestions</button><small>{data.sceneAnalysisStatus.pendingSceneJobs} scene scans pending</small></div><CandidateReviews kind="Location" candidates={locationCandidates} targets={data.locations.map(location => ({ id: location.id, name: location.name, hasReferences: location.confirmedPhotoCount > 0 }))} onReview={reviewCandidate} onReviewAsNew={reviewCandidateAsNew} assetsById={assetsById} showPhoto /><KnownLocations locations={data.locations} assetsById={assetsById} collapsed={knownLocationsCollapsed} onToggle={toggleKnownLocations} onRevoke={revokeLocationPhoto} /></PhotoAnalysisSubsection>
      <PhotoAnalysisSubsection title="Events" count={data.events.length} collapsed={eventsCollapsed} onToggle={toggleEvents}><form className="event-form" onSubmit={(event) => { event.preventDefault(); if (eventTitle.trim()) { addEvent(eventTitle, eventDate || null, eventLocation || null, personIds, assetIds); setEventTitle(''); setEventDate(''); setEventLocation(''); setPersonIds([]); setAssetIds([]) } }}><div className="event-form-row"><input className="field field-xs" value={eventTitle} onChange={event => setEventTitle(event.target.value)} placeholder="Event title" aria-label="Event title" /><input className="field field-xs" type="date" value={eventDate} onChange={event => setEventDate(event.target.value)} aria-label="Event date" /><Select ariaLabel="Event location" value={eventLocation} onChange={setEventLocation} placeholder="No location yet" options={data.locations.map(location => ({ id: location.id, label: location.name }))} /></div><MultiSelect ariaLabel="Event people" placeholder={personIds.length === 0 ? 'Add people…' : 'Add more people…'} options={data.people.map(person => ({ id: person.id, label: person.name }))} selected={personIds} onChange={setPersonIds} /><PhotoMultiSelect ariaLabel="Event photos" addLabel={assetIds.length === 0 ? 'Add photos…' : 'Add more photos…'} searchPlaceholder="Search photos…" photos={assets.map(asset => ({ id: asset.id, name: asset.originalFileName, storedFileName: asset.storedFileName }))} selected={assetIds} onChange={setAssetIds} /><div className="event-form-footer"><button className="btn btn-xs">Create event</button></div></form><div className="face-analysis-action"><button className="btn btn-xs" type="button" onClick={queueEventAnalysis}>Refresh event candidates</button><small>{data.eventAnalysisStatus.pendingEventJobs} event scans pending</small></div><EventCandidateReviews candidates={data.pendingEventCandidates} events={data.events.map(item => ({ id: item.id, name: item.title }))} locations={data.locations} people={data.people} assetsById={assetsById} onReview={reviewEventCandidate} /><div className="face-analysis-action"><button className="btn btn-xs" type="button" onClick={queueSceneObservations}>Refresh scene observations</button><small>{data.observationAnalysisStatus.pendingObservationJobs} observation scans pending</small></div><SceneObservationReviews observations={data.pendingSceneObservations} assetsById={assetsById} onReview={reviewSceneObservation} /><CandidateReviews kind="Event" candidates={eventCandidates} targets={data.events.map(item => ({ id: item.id, name: item.title }))} onReview={reviewCandidate} onReviewAsNew={reviewCandidateAsNew} assetsById={assetsById} /><KnownEvents events={data.events} assetsById={assetsById} collapsed={knownEventsCollapsed} onToggle={toggleKnownEvents} onRevoke={detachEventPhoto} /></PhotoAnalysisSubsection>
    </div>
  </section>
}
