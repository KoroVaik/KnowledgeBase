import { useEffect, useState, type ReactNode } from 'react'
import type { EventCandidate, FaceBounds, Location, Person, PhotoAnalysisCandidate, SceneObservation } from '../../api/photoAnalysis'
import { fetchDownloadUrl } from '../../api/assets'
import type { AssetSummary } from '../../api/assets'
import { useCollapsibleSection } from '../../hooks/useCollapsibleSection'
import { usePhotoAnalysisSection } from './usePhotoAnalysisSection'
import './PhotoAnalysisSection.css'

interface ReviewTarget { id: string; name: string }

function CandidateThumbnail({ asset }: { asset: AssetSummary | undefined }) {
  const [url, setUrl] = useState<string | null>(null)
  const [unavailable, setUnavailable] = useState(false)
  useEffect(() => {
    if (asset === undefined) return
    let cancelled = false
    void fetchDownloadUrl(asset.storedFileName).then(link => { if (!cancelled) setUrl(link) }).catch(() => { if (!cancelled) setUnavailable(true) })
    return () => { cancelled = true }
  }, [asset])
  if (url) return <div className="candidate-thumbnail"><img src={url} alt={`Photo ${asset?.originalFileName ?? ''}`} /></div>
  return <div className="candidate-thumbnail"><span>{asset && !unavailable ? 'Loading preview…' : 'Photo preview unavailable'}</span></div>
}

function PersonCandidatePreview({ asset, faceBounds }: { asset: AssetSummary | undefined; faceBounds: FaceBounds }) {
  const [url, setUrl] = useState<string | null>(null)
  const [unavailable, setUnavailable] = useState(false)
  const [sourceSize, setSourceSize] = useState<{ width: number; height: number } | null>(null)
  useEffect(() => {
    if (asset === undefined) return
    let cancelled = false
    void fetchDownloadUrl(asset.storedFileName).then(link => { if (!cancelled) setUrl(link) }).catch(() => { if (!cancelled) setUnavailable(true) })
    return () => { cancelled = true }
  }, [asset])

  if (url === null) return <div className="candidate-thumbnail"><span>{asset && !unavailable ? 'Loading preview…' : 'Photo preview unavailable'}</span></div>

  const topPercent = sourceSize === null ? 0 : Math.max(0, faceBounds.y / sourceSize.height * 100)
  const frameStyle = sourceSize === null ? undefined : {
    left: `${Math.max(0, faceBounds.x / sourceSize.width * 100)}%`,
    top: `${topPercent}%`,
    width: `${Math.min(100, faceBounds.width / sourceSize.width * 100)}%`,
    height: `${Math.min(100, faceBounds.height / sourceSize.height * 100)}%`,
  }
  // Not enough room above the box near the top edge of the photo — put the label under it instead.
  const labelBelow = topPercent < 15
  const crop = sourceSize === null ? null : faceCrop(faceBounds, sourceSize)

  return <div className="person-candidate-previews">
    <figure className="face-preview-panel">
      <figcaption>Original photo</figcaption>
      <div className="face-frame-preview">
        <img src={url} alt={`Detected face in ${asset?.originalFileName ?? 'photo'}`} onLoad={event => setSourceSize({ width: event.currentTarget.naturalWidth, height: event.currentTarget.naturalHeight })} />
        {frameStyle && <span className={`face-frame${labelBelow ? ' face-frame-label-below' : ''}`} style={frameStyle}><span>Reviewing this face</span></span>}
      </div>
    </figure>
    <figure className="face-preview-panel">
      <figcaption>Face crop</figcaption>
      <div className="face-crop-preview">
        {crop === null ? <span>Loading face…</span> : <img src={url} alt="" aria-hidden="true" style={crop} />}
      </div>
    </figure>
  </div>
}

function faceCrop(bounds: FaceBounds, source: { width: number; height: number }) {
  const faceWidth = Math.min(bounds.width, source.width)
  const faceHeight = Math.min(bounds.height, source.height)
  const side = Math.min(Math.max(faceWidth, faceHeight) * 1.3, source.width, source.height)
  const centerX = Math.min(Math.max(bounds.x + bounds.width / 2, side / 2), source.width - side / 2)
  const centerY = Math.min(Math.max(bounds.y + bounds.height / 2, side / 2), source.height - side / 2)
  const scale = 144 / side
  return { width: source.width * scale, height: source.height * scale, left: -(centerX - side / 2) * scale, top: -(centerY - side / 2) * scale }
}

function CandidateReviews({ candidates, targets, onReview, assetsById, showPhoto = false }: { candidates: PhotoAnalysisCandidate[]; targets: ReviewTarget[]; onReview: (candidateId: string, kind: string, chosenTargetId: string | null) => void; assetsById: Map<string, AssetSummary>; showPhoto?: boolean }) {
  const [corrections, setCorrections] = useState<Record<string, string>>({})
  if (candidates.length === 0) return null
  return <div className="candidate-reviews"><h4>To review <span className="section-count">{candidates.length}</span></h4>{candidates.map(candidate => {
    const faceBounds = showPhoto && candidate.kind === 'Person' ? candidate.faceBounds : null
    return <article className={faceBounds != null ? 'candidate-review candidate-review-with-face-preview' : showPhoto ? 'candidate-review candidate-review-with-image' : 'candidate-review'} key={candidate.id}>
    {showPhoto && (faceBounds != null ? <PersonCandidatePreview key={candidate.subjectFaceOccurrenceId} asset={assetsById.get(candidate.subjectAssetId)} faceBounds={faceBounds} /> : <CandidateThumbnail key={candidate.subjectAssetId} asset={assetsById.get(candidate.subjectAssetId)} />)}
    <div className="candidate-review-details"><strong>{candidate.subjectAssetName}</strong>
      <span>Proposal: {candidate.proposedTargetName ?? candidate.proposedLabel ?? 'No existing record'} · score {candidate.score.toFixed(3)} · #{candidate.rank}</span>
      <details><summary>Model evidence</summary><pre>{candidate.signalsJson}</pre></details>
      <div className="candidate-actions">
        {candidate.proposedTargetId && <button className="btn btn-xs" type="button" onClick={() => onReview(candidate.id, 'Accepted', null)}>Accept</button>}
        <button className="btn btn-xs" type="button" onClick={() => onReview(candidate.id, 'Rejected', null)}>Reject</button>
        <select className="field field-xs" value={corrections[candidate.id] ?? ''} onChange={event => setCorrections(current => ({ ...current, [candidate.id]: event.target.value }))}>
          <option value="">Correct to…</option>{targets.map(target => <option key={target.id} value={target.id}>{target.name}</option>)}
        </select>
        <button className="btn btn-xs" type="button" disabled={!corrections[candidate.id]} onClick={() => onReview(candidate.id, 'Corrected', corrections[candidate.id])}>Save correction</button>
      </div>
    </div>
  </article>})}</div>
}

function PhotoAnalysisSubsection({ title, count, collapsed, onToggle, children }: { title: string; count: number; collapsed: boolean; onToggle: () => void; children: ReactNode }) {
  return <section className="subsection-panel photo-analysis-subsection"><h3 className="tags-subhead"><button type="button" className="subsection-toggle" aria-expanded={!collapsed} onClick={onToggle}><span className="section-toggle-caret" aria-hidden="true">▾</span>{title}<span className="section-count">{count}</span></button></h3>{!collapsed && children}</section>
}

function SceneObservationReviews({ observations, onReview }: { observations: SceneObservation[]; onReview: (observationId: string, kind: string) => void }) {
  if (observations.length === 0) return null
  return <div className="candidate-reviews"><h4>Scene observations <span className="section-count">{observations.length}</span></h4>{observations.map(observation => <article className="candidate-review" key={observation.id}>
    <strong>{observation.assetName} · {observation.kind}</strong>
    <span>{[observation.subjectPersonName, observation.relatedPersonName].filter(Boolean).join(' → ') || 'No named person'} · confidence {observation.confidence.toFixed(2)}</span>
    <p>{observation.description}</p>
    <details><summary>Visible evidence</summary><p>{observation.evidence}</p></details>
    <div className="candidate-actions"><button className="btn btn-xs" type="button" onClick={() => onReview(observation.id, 'Confirmed')}>Confirm</button><button className="btn btn-xs" type="button" onClick={() => onReview(observation.id, 'Rejected')}>Reject</button></div>
  </article>)}</div>
}

function EventCandidateReviews({ candidates, events, locations, people, onReview }: { candidates: EventCandidate[]; events: ReviewTarget[]; locations: Location[]; people: Person[]; onReview: (candidateId: string, body: { kind: string; chosenEventId: string | null; title: string | null; occurredOn: string | null; locationId: string | null; personIds: string[]; assetIds: string[] }) => void }) {
  const [selectedPhotos, setSelectedPhotos] = useState<Record<string, string[]>>({})
  const [titles, setTitles] = useState<Record<string, string>>({})
  const [dates, setDates] = useState<Record<string, string>>({})
  const [locationsByCandidate, setLocationsByCandidate] = useState<Record<string, string>>({})
  const [peopleByCandidate, setPeopleByCandidate] = useState<Record<string, string[]>>({})
  const [existingEvents, setExistingEvents] = useState<Record<string, string>>({})
  if (candidates.length === 0) return null
  const toggle = (id: string, current: string[], set: (next: string[]) => void) => set(current.includes(id) ? current.filter(value => value !== id) : [...current, id])
  return <div className="candidate-reviews"><h4>Possible events <span className="section-count">{candidates.length}</span></h4>{candidates.map(candidate => {
    const photos = selectedPhotos[candidate.id] ?? candidate.photos.map(photo => photo.id)
    const candidatePeople = peopleByCandidate[candidate.id] ?? []
    return <article className="candidate-review" key={candidate.id}>
      <strong>{candidate.photos.length} photos · score {candidate.score.toFixed(3)}</strong>
      <span>{candidate.photos.map(photo => photo.name).join(', ')}</span>
      <details><summary>Clustering evidence</summary><pre>{candidate.signalsJson}</pre></details>
      <fieldset><legend>Include photos</legend>{candidate.photos.map(photo => <label key={photo.id}><input type="checkbox" checked={photos.includes(photo.id)} onChange={() => toggle(photo.id, photos, next => setSelectedPhotos(current => ({ ...current, [candidate.id]: next })))} /> {photo.name}</label>)}</fieldset>
      <input className="field field-xs" value={titles[candidate.id] ?? ''} onChange={event => setTitles(current => ({ ...current, [candidate.id]: event.target.value }))} placeholder="New event title" />
      <input className="field field-xs" type="date" value={dates[candidate.id] ?? candidate.suggestedOccurredOn ?? ''} onChange={event => setDates(current => ({ ...current, [candidate.id]: event.target.value }))} />
      <select className="field field-xs" value={locationsByCandidate[candidate.id] ?? ''} onChange={event => setLocationsByCandidate(current => ({ ...current, [candidate.id]: event.target.value }))}><option value="">No location</option>{locations.map(location => <option key={location.id} value={location.id}>{location.name}</option>)}</select>
      <fieldset><legend>People</legend>{people.map(person => <label key={person.id}><input type="checkbox" checked={candidatePeople.includes(person.id)} onChange={() => toggle(person.id, candidatePeople, next => setPeopleByCandidate(current => ({ ...current, [candidate.id]: next })))} /> {person.name}</label>)}</fieldset>
      <div className="candidate-actions"><button className="btn btn-xs" type="button" disabled={!titles[candidate.id]?.trim() || photos.length === 0} onClick={() => onReview(candidate.id, { kind: 'Created', chosenEventId: null, title: titles[candidate.id], occurredOn: dates[candidate.id] ?? candidate.suggestedOccurredOn ?? null, locationId: locationsByCandidate[candidate.id] || null, personIds: candidatePeople, assetIds: photos })}>Create event</button><select className="field field-xs" value={existingEvents[candidate.id] ?? ''} onChange={event => setExistingEvents(current => ({ ...current, [candidate.id]: event.target.value }))}><option value="">Add to existing…</option>{events.map(event => <option key={event.id} value={event.id}>{event.name}</option>)}</select><button className="btn btn-xs" type="button" disabled={!existingEvents[candidate.id] || photos.length === 0} onClick={() => onReview(candidate.id, { kind: 'Attached', chosenEventId: existingEvents[candidate.id], title: null, occurredOn: null, locationId: null, personIds: [], assetIds: photos })}>Add selected photos</button><button className="btn btn-xs" type="button" onClick={() => onReview(candidate.id, { kind: 'Rejected', chosenEventId: null, title: null, occurredOn: null, locationId: null, personIds: [], assetIds: [] })}>Reject</button></div>
    </article>
  })}</div>
}

export function PhotoAnalysisSection() {
  const { data, assets, error, addPerson, addLocation, addEvent, reviewCandidate, reviewSceneObservation, reviewEventCandidate, queueFaceAnalysis, queueSceneAnalysis, queueSceneObservations, queueEventAnalysis } = usePhotoAnalysisSection()
  const { collapsed: peopleCollapsed, toggle: togglePeople } = useCollapsibleSection('photo-analysis:people', false)
  const { collapsed: locationsCollapsed, toggle: toggleLocations } = useCollapsibleSection('photo-analysis:locations', false)
  const { collapsed: eventsCollapsed, toggle: toggleEvents } = useCollapsibleSection('photo-analysis:events', false)
  const [personName, setPersonName] = useState('')
  const [locationName, setLocationName] = useState('')
  const [locationKind, setLocationKind] = useState('Physical')
  const [eventTitle, setEventTitle] = useState('')
  const [eventDate, setEventDate] = useState('')
  const [eventLocation, setEventLocation] = useState('')
  const [personIds, setPersonIds] = useState<string[]>([])
  const [assetIds, setAssetIds] = useState<string[]>([])
  const toggle = (id: string, current: string[], set: (next: string[]) => void) => set(current.includes(id) ? current.filter(value => value !== id) : [...current, id])

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
      <PhotoAnalysisSubsection title="Persons" count={data.people.length} collapsed={peopleCollapsed} onToggle={togglePeople}><form onSubmit={(event) => { event.preventDefault(); if (personName.trim()) { addPerson(personName); setPersonName('') } }}><input className="field field-xs" value={personName} onChange={event => setPersonName(event.target.value)} placeholder="Name" /><button className="btn btn-xs">Add</button></form><div className="face-analysis-action"><button className="btn btn-xs" type="button" onClick={queueFaceAnalysis}>Analyze unprocessed photos</button><small>{data.faceAnalysisStatus.imagesWithoutFingerprint > 0 ? `${data.faceAnalysisStatus.imagesWithoutFingerprint} images need fingerprinting` : `${data.faceAnalysisStatus.pendingFaceJobs} face scans pending`}</small></div><CandidateReviews candidates={personCandidates} targets={data.people} onReview={reviewCandidate} assetsById={assetsById} showPhoto /><ul>{data.people.map(person => <li key={person.id}>{person.name}<small>{person.eventCount} events</small></li>)}</ul></PhotoAnalysisSubsection>
      <PhotoAnalysisSubsection title="Locations" count={data.locations.length} collapsed={locationsCollapsed} onToggle={toggleLocations}><form onSubmit={(event) => { event.preventDefault(); if (locationName.trim()) { addLocation(locationName, locationKind); setLocationName('') } }}><input className="field field-xs" value={locationName} onChange={event => setLocationName(event.target.value)} placeholder="Location" /><select className="field field-xs" value={locationKind} onChange={event => setLocationKind(event.target.value)}><option>Physical</option><option>Visual</option></select><button className="btn btn-xs">Add</button></form><div className="face-analysis-action"><button className="btn btn-xs" type="button" onClick={queueSceneAnalysis}>Refresh location suggestions</button><small>{data.sceneAnalysisStatus.pendingSceneJobs} scene scans pending</small></div><CandidateReviews candidates={locationCandidates} targets={data.locations} onReview={reviewCandidate} assetsById={assetsById} showPhoto /><ul>{data.locations.map(location => <li key={location.id}>{location.name}<small>{location.kind.toLowerCase()} · {location.confirmedPhotoCount} confirmed photos · {location.eventCount} events</small></li>)}</ul></PhotoAnalysisSubsection>
      <PhotoAnalysisSubsection title="Events" count={data.events.length} collapsed={eventsCollapsed} onToggle={toggleEvents}><form className="event-form" onSubmit={(event) => { event.preventDefault(); if (eventTitle.trim()) { addEvent(eventTitle, eventDate || null, eventLocation || null, personIds, assetIds); setEventTitle(''); setEventDate(''); setEventLocation(''); setPersonIds([]); setAssetIds([]) } }}><input className="field field-xs" value={eventTitle} onChange={event => setEventTitle(event.target.value)} placeholder="Event title" /><input className="field field-xs" type="date" value={eventDate} onChange={event => setEventDate(event.target.value)} /><select className="field field-xs" value={eventLocation} onChange={event => setEventLocation(event.target.value)}><option value="">No location yet</option>{data.locations.map(location => <option key={location.id} value={location.id}>{location.name}</option>)}</select><fieldset><legend>People</legend>{data.people.map(person => <label key={person.id}><input type="checkbox" checked={personIds.includes(person.id)} onChange={() => toggle(person.id, personIds, setPersonIds)} /> {person.name}</label>)}</fieldset><fieldset><legend>Photos</legend>{assets.map(asset => <label key={asset.id}><input type="checkbox" checked={assetIds.includes(asset.id)} onChange={() => toggle(asset.id, assetIds, setAssetIds)} /> {asset.originalFileName}</label>)}</fieldset><button className="btn btn-xs">Create event</button></form><div className="face-analysis-action"><button className="btn btn-xs" type="button" onClick={queueEventAnalysis}>Refresh event candidates</button><small>{data.eventAnalysisStatus.pendingEventJobs} event scans pending</small></div><EventCandidateReviews candidates={data.pendingEventCandidates} events={data.events.map(item => ({ id: item.id, name: item.title }))} locations={data.locations} people={data.people} onReview={reviewEventCandidate} /><div className="face-analysis-action"><button className="btn btn-xs" type="button" onClick={queueSceneObservations}>Refresh scene observations</button><small>{data.observationAnalysisStatus.pendingObservationJobs} observation scans pending</small></div><SceneObservationReviews observations={data.pendingSceneObservations} onReview={reviewSceneObservation} /><CandidateReviews candidates={eventCandidates} targets={data.events.map(item => ({ id: item.id, name: item.title }))} onReview={reviewCandidate} assetsById={assetsById} /><ul>{data.events.map(item => <li key={item.id}>{item.title}<small>{item.occurredOn ?? 'No date'} · {item.locationName ?? 'No location'} · {item.assetIds.length} photos</small></li>)}</ul></PhotoAnalysisSubsection>
    </div>
  </section>
}
