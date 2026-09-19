import { useState } from 'react'
import type { FaceBounds, Person, PeopleReviewFace, PeopleReviewHint, PersonReferenceFace } from '../../api/photoAnalysis'
import type { AssetSummary } from '../../api/assets'
import { useCollapsibleSection } from '../../hooks/useCollapsibleSection'
import { FaceCropPreview, PhotoPopupDialog, type PhotoPopupTarget } from '../FacePreview/FacePreview'
import { PersonNamePicker } from './PersonNamePicker'
import { usePeopleReview } from './usePeopleReview'
import './PeopleReviewSection.css'

const CROP_SIZE = 64

type OpenPhoto = (title: string, assetId: string, faceBounds: FaceBounds) => void

function FaceStrip({ confirmed = [], faces, title, assetsById, onOpen, review }: {
  confirmed?: PersonReferenceFace[]
  faces: PeopleReviewFace[]
  title: string
  assetsById: Map<string, AssetSummary>
  onOpen: OpenPhoto
  review?: ReturnType<typeof usePeopleReview>
}) {
  return <div className="people-review-strip">
    {confirmed.map(face => <div key={face.id} className="people-review-face">
      <button type="button" className="people-review-crop" aria-label={`View confirmed photo of ${title}`} onClick={() => onOpen(title, face.assetId, face.faceBounds)}>
        <FaceCropPreview asset={assetsById.get(face.assetId)} faceBounds={face.faceBounds} size={CROP_SIZE} />
        <span className="people-review-check" aria-label="Confirmed">✓</span>
      </button>
    </div>)}
    {faces.map(face => <div key={face.candidateId} className="people-review-face">
      <button type="button" className="people-review-crop" aria-label={`View full photo for ${title}`} onClick={() => onOpen(title, face.assetId, face.faceBounds)}>
        <FaceCropPreview asset={assetsById.get(face.assetId)} faceBounds={face.faceBounds} size={CROP_SIZE} />
      </button>
      {review && <input type="checkbox" className="people-review-keep" checked={review.isChecked(face.candidateId)} onChange={() => review.toggleFace(face.candidateId)}
        aria-label="Belongs to this person" title="Unchecked faces move to Unsorted on submit" />}
    </div>)}
  </div>
}

function HintChip({ hint, onUse }: { hint: PeopleReviewHint | null; onUse: (name: string) => void }) {
  if (hint === null) return null
  return <button type="button" className="people-review-hint" title="Use this name" onClick={() => onUse(hint.name)}>Looks like: {hint.name}</button>
}

function pickerId(rowKey: string) {
  return `person-picker-${rowKey.replace(/[^a-zA-Z0-9-]/g, '-')}`
}

/** A row without a person yet: picking a name files the checked faces at once, or the row is ignored. */
function NameRowActions({ rowKey, faces, canIgnore, people, review }: {
  rowKey: string
  faces: PeopleReviewFace[]
  canIgnore: boolean
  people: Person[]
  review: ReturnType<typeof usePeopleReview>
}) {
  const busy = review.isBusy(rowKey)
  const checked = review.checkedCount(faces)
  return <div className="people-review-actions">
    <PersonNamePicker inputId={pickerId(rowKey)} people={people} value={review.nameFor(rowKey)} disabled={busy || checked === 0}
      onChange={value => review.setName(rowKey, value)}
      onPickPerson={person => review.submitToPerson(rowKey, faces, person.id)}
      onAddName={name => review.submitByName(rowKey, faces, name)} />
    {canIgnore && <button type="button" className="btn btn-xs people-review-ignore" disabled={busy || checked === 0} onClick={() => review.ignore(rowKey, faces)}>Ignore</button>}
  </div>
}

export function PeopleReviewSection({ people, assetsById }: { people: Person[]; assetsById: Map<string, AssetSummary> }) {
  const review = usePeopleReview()
  const { collapsed: ignoredCollapsed, toggle: toggleIgnored } = useCollapsibleSection('photo-analysis:ignored-faces')
  const [openPhoto, setOpenPhoto] = useState<PhotoPopupTarget | null>(null)
  const open: OpenPhoto = (title, assetId, faceBounds) => setOpenPhoto({ title, assetId, faceBounds })
  const applyHint = (rowKey: string, name: string) => {
    review.setName(rowKey, name)
    document.getElementById(pickerId(rowKey))?.focus()
  }
  const data = review.review

  if (data === null) return review.error ? <p className="notes-error">{review.error}</p> : null
  const { personRows, anonymousRows, unsorted, ignoredGroups } = data
  const rowCount = personRows.length + anonymousRows.length + unsorted.length
  if (rowCount === 0 && ignoredGroups.length === 0 && !data.clusteringPending && review.error === null) return null

  return <div className="people-review">
    <h4>To review <span className="section-count">{rowCount}</span></h4>
    {data.clusteringPending && <p className="people-review-pending">Grouping faces…</p>}
    {review.error && <p className="notes-error">{review.error}</p>}

    {personRows.map(row => {
      const rowKey = `person:${row.personId}`
      return <article key={rowKey} className="people-review-row">
        <div className="people-review-row-head">
          <span className="people-review-title">{row.name}</span>
          <small>{row.faces.length} new · {row.referenceFaceCount} confirmed</small>
        </div>
        <div className="people-review-band">
          <span className="people-review-band-label">Approved faces</span>
          <FaceStrip confirmed={row.referenceFaces} faces={[]} title={row.name} assetsById={assetsById} onOpen={open} />
        </div>
        <div className="people-review-band">
          <span className="people-review-band-label">New suggested faces</span>
          <FaceStrip faces={row.faces} title={row.name} assetsById={assetsById} onOpen={open} review={review} />
        </div>
        <div className="people-review-actions">
          <button type="button" className="btn btn-xs btn-primary" disabled={review.isBusy(rowKey) || review.checkedCount(row.faces) === 0} onClick={() => review.submitToPerson(rowKey, row.faces, row.personId)}>Submit person</button>
        </div>
      </article>
    })}

    {anonymousRows.map(row => {
      const rowKey = `cluster:${row.clusterId}`
      return <article key={rowKey} className="people-review-row">
        <div className="people-review-row-head">
          <span className="people-review-title">Unknown person</span>
          <small>{row.faces.length} faces</small>
          <HintChip hint={row.hint} onUse={name => applyHint(rowKey, name)} />
        </div>
        <FaceStrip faces={row.faces} title="Unknown person" assetsById={assetsById} onOpen={open} review={review} />
        <NameRowActions rowKey={rowKey} faces={row.faces} canIgnore people={people} review={review} />
      </article>
    })}

    {unsorted.length > 0 && <article className="people-review-row">
      <div className="people-review-row-head">
        <span className="people-review-title">Unsorted faces</span>
        <small>{unsorted.length} · faces that didn't match anyone</small>
      </div>
      <div className="people-review-unsorted">{unsorted.map(face => {
        const rowKey = `face:${face.candidateId}`
        return <article key={rowKey} className="people-review-row people-review-single">
          <FaceStrip faces={[face]} title="Unsorted face" assetsById={assetsById} onOpen={open} />
          <NameRowActions rowKey={rowKey} faces={[face]} canIgnore people={people} review={review} />
        </article>
      })}</div>
    </article>}

    {ignoredGroups.length > 0 && <section className="people-review-group">
      <h5><button type="button" className="people-review-toggle" aria-expanded={!ignoredCollapsed} onClick={toggleIgnored}><span className="people-review-caret" aria-hidden="true">▾</span>Ignored <span className="section-count">{ignoredGroups.length}</span></button></h5>
      {!ignoredCollapsed && ignoredGroups.map(group => {
        const rowKey = `ignored:${group.groupId}`
        return <article key={rowKey} className="people-review-row">
          <div className="people-review-row-head">
            <span className="people-review-title">Ignored faces</span>
            <small>{group.faces.length} faces</small>
            <HintChip hint={group.hint} onUse={name => applyHint(rowKey, name)} />
          </div>
          <FaceStrip faces={group.faces} title="Ignored faces" assetsById={assetsById} onOpen={open} review={review} />
          <NameRowActions rowKey={rowKey} faces={group.faces} canIgnore={false} people={people} review={review} />
        </article>
      })}
    </section>}

    <PhotoPopupDialog target={openPhoto} assetsById={assetsById} onClose={() => setOpenPhoto(null)} />
  </div>
}
