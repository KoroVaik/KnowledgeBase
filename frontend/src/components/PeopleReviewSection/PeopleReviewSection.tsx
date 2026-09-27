import { SectionReload } from '../GenericList/SectionReload'
import { GenericList } from '../GenericList/GenericList'
import { useListPresence } from '../GenericList/useListPresence'
import { useState } from 'react'
import type { FaceBounds, Person, PeopleReviewFace, PeopleReviewHint, PersonReferenceFace } from '../../api/photoAnalysis'
import type { AssetSummary } from '../../api/assets'
import { useCollapsibleSection } from '../../hooks/useCollapsibleSection'
import { FaceCropPreview, PhotoPopupDialog, type PhotoPopupTarget } from '../FacePreview/FacePreview'
import { PersonNamePicker } from './PersonNamePicker'
import { usePeopleReview } from './usePeopleReview'
import { FaceValidationDetails } from './FaceValidationDetails'
import { IgnoredFaceGallery } from './IgnoredFaceGallery'
import { ExpandableBadge } from '../ExpandableBadge/ExpandableBadge'
import './PeopleReviewSection.css'

const CROP_SIZE = 64
const warningIcon = <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" aria-hidden="true" focusable="false">
  <path d="M8 1.5 15 14H1Z" />
  <path d="M8 5.5v4" strokeLinecap="round" />
  <circle cx="8" cy="11.5" r=".75" fill="currentColor" stroke="none" />
</svg>

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
      <div className="people-review-photo">
        <button type="button" className="people-review-crop" aria-label={`View full photo for ${title}`} onClick={() => onOpen(title, face.assetId, face.faceBounds)}>
          <FaceCropPreview asset={assetsById.get(face.assetId)} faceBounds={face.faceBounds} size={CROP_SIZE} />
        </button>
        {face.isPartial && !face.needsReview && <span className="people-review-badge"><ExpandableBadge label="Photo edge" color="#fbbf2499" icon={warningIcon} /></span>}
        {face.needsReview && <span className="people-review-badge"><ExpandableBadge label="Needs review" color="#fb923c99" icon={warningIcon} /></span>}
      </div>
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
function NameRowActions({ rowKey, faces, canIgnore, people, review, showSelection = false, explainAssignment = false }: {
  rowKey: string
  faces: PeopleReviewFace[]
  canIgnore: boolean
  people: Person[]
  review: ReturnType<typeof usePeopleReview>
  showSelection?: boolean
  explainAssignment?: boolean
}) {
  const busy = review.isBusy(rowKey)
  const checked = review.checkedCount(faces)
  const actions = <div className="people-review-actions">
    <PersonNamePicker inputId={pickerId(rowKey)} people={people} value={review.nameFor(rowKey)} disabled={busy || checked === 0}
      onChange={value => review.setName(rowKey, value)}
      onPickPerson={person => review.submitToPerson(rowKey, faces, person.id)}
      onAddName={name => review.submitByName(rowKey, faces, name)} />
    {canIgnore && <button type="button" className="btn btn-xs people-review-ignore" disabled={busy || checked === 0} title="Move the selected faces to Ignored to review later." onClick={() => review.ignore(rowKey, faces)}>Ignore</button>}
  </div>
  if (!showSelection && !explainAssignment) return actions
  return <div className="people-review-assignment">
    {showSelection && <p className="people-review-selection-count">Selected: {checked} of {faces.length}</p>}
    {actions}
    <p className="people-review-assignment-help">{checked === 0
      ? 'Select at least one face to assign a person.'
      : showSelection
        ? 'Choose who is in the selected photos. This also confirms these are human faces. Deselected faces move to Unsorted.'
        : 'Choose who is in the photo. This also confirms it is a human face.'}</p>
  </div>
}

export function PeopleReviewSection({ people, assetsById }: { people: Person[]; assetsById: Map<string, AssetSummary> }) {
  const { collapsed: reviewCollapsed, toggle: toggleReview } = useCollapsibleSection('photo-analysis:people-review', false)
  const { collapsed: unsortedCollapsed, toggle: toggleUnsorted } = useCollapsibleSection('photo-analysis:unsorted-faces', false)
  const { collapsed: ignoredCollapsed, toggle: toggleIgnored } = useCollapsibleSection('photo-analysis:ignored-faces')
  const review = usePeopleReview(reviewCollapsed && unsortedCollapsed && ignoredCollapsed)
  const [openPhoto, setOpenPhoto] = useState<PhotoPopupTarget | null>(null)
  const open: OpenPhoto = (title, assetId, faceBounds) => setOpenPhoto({ title, assetId, faceBounds })
  const applyHint = (rowKey: string, name: string) => {
    review.setName(rowKey, name)
    document.getElementById(pickerId(rowKey))?.focus()
  }
  const data = review.review
  const hasUnsorted = useListPresence((data?.unsorted.length ?? 0) > 0)
  const hasIgnored = useListPresence((data?.ignoredGroups.length ?? 0) > 0)

  if (data === null) return <div className="people-review" {...review.refresh.bind}>
    <SectionReload {...review.refresh} error={review.error} reload={review.reloadSection} />
    {[
      { title: 'To review', collapsed: reviewCollapsed, toggle: toggleReview },
      { title: 'Unsorted faces', collapsed: unsortedCollapsed, toggle: toggleUnsorted },
      { title: 'Ignored', collapsed: ignoredCollapsed, toggle: toggleIgnored },
    ].map(section => <section key={section.title} className="subsection-panel">
      <h4 className="tags-subhead"><button type="button" className="subsection-toggle" aria-expanded={!section.collapsed} onClick={section.toggle}>{section.title}</button></h4>
      {!section.collapsed && <p>Loading people review…</p>}
    </section>)}
  </div>
  const { personRows, anonymousRows, unsorted, ignoredGroups } = data

  const latest = review.latest ?? data
  const newRows = review.newRows([...latest.personRows, ...latest.anonymousRows])
  const reviewUpdates = review.refresh.additions([...personRows, ...anonymousRows], [...personRows, ...anonymousRows, ...newRows])
  const unsortedUpdates = review.refresh.additions(unsorted, latest.unsorted.filter(face => unsorted.some(old => old.candidateId === face.candidateId) || ![...personRows, ...anonymousRows, ...ignoredGroups].some(row => row.faces.some(old => old.candidateId === face.candidateId))))
  const ignoredUpdates = review.refresh.additions(ignoredGroups, [...ignoredGroups, ...review.newRows(latest.ignoredGroups)])
  return <div className="people-review" {...review.refresh.bind}>
    <SectionReload {...review.refresh} error={review.error} reload={review.reloadSection} />
    <section className="subsection-panel">
    <h4 className="tags-subhead people-review-collapsible-head"><button type="button" className="subsection-toggle people-review-toggle" aria-expanded={!reviewCollapsed} onClick={toggleReview}><span className="people-review-caret" aria-hidden="true">▾</span>To review <span className="section-count">{personRows.length + anonymousRows.length}</span></button></h4>
    {!reviewCollapsed && <>
    {data.clusteringPending && <p className="people-review-pending">Grouping faces…</p>}
    {data.validationPending && <p className="people-review-pending">Validating faces… Pending faces stay out of automatic groups.</p>}
    {review.error && <p className="notes-error">{review.error}</p>}

    <GenericList updates={reviewUpdates} animated listId="people:review" items={[
      ...personRows.map(row => ({ kind: 'person' as const, row })),
      ...anonymousRows.map(row => ({ kind: 'anonymous' as const, row })),
    ]}>{shownItems => <>
    {shownItems.map(item => {
      if (item.kind !== 'person') return null
      const row = item.row
      const rowKey = `person:${row.personId}`
      return <article key={rowKey} className="people-review-row" aria-busy={review.isBusy(rowKey)}>
        <div className="people-review-row-head">
          <span className="people-review-title">{row.name}</span>
          <small>{row.faces.length} new · {row.referenceFaceCount} confirmed</small>
          {review.isBusy(rowKey) && <small role="status">Saving…</small>}
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
          {review.checkedCount(row.faces) === 0
            ? <button type="button" className="btn btn-xs btn-danger" disabled={review.isBusy(rowKey)} title="Send these faces to Ignored" onClick={() => review.rejectAll(rowKey, row.faces)}>Reject</button>
            : <button type="button" className="btn btn-xs btn-primary" disabled={review.isBusy(rowKey)} onClick={() => review.submitToPerson(rowKey, row.faces, row.personId)}>Submit person</button>}
        </div>
      </article>
    })}

    {shownItems.map(item => {
      if (item.kind !== 'anonymous') return null
      const row = item.row
      const rowKey = `cluster:${row.clusterId}`
      return <article key={rowKey} className="people-review-row" aria-busy={review.isBusy(rowKey)}>
        <div className="people-review-row-head">
          <span className="people-review-title">Unknown person</span>
          <small>{row.faces.length} faces</small>
          {review.isBusy(rowKey) && <small role="status">Saving…</small>}
          <HintChip hint={row.hint} onUse={name => applyHint(rowKey, name)} />
        </div>
        <FaceStrip faces={row.faces} title="Unknown person" assetsById={assetsById} onOpen={open} review={review} />
        <NameRowActions rowKey={rowKey} faces={row.faces} canIgnore people={people} review={review} />
      </article>
    })}
    </>}</GenericList>
    </>}
    </section>

    {(hasUnsorted || unsortedUpdates.count > 0) && <section className="subsection-panel">
      <h4 className="tags-subhead people-review-collapsible-head"><button type="button" className="subsection-toggle people-review-toggle" aria-expanded={!unsortedCollapsed} onClick={toggleUnsorted}><span className="people-review-caret" aria-hidden="true">▾</span>Unsorted faces <span className="section-count">{unsorted.length}</span></button></h4>
      {!unsortedCollapsed && <>
      <p className="people-review-pending">Unmatched faces and validation checks</p>
      <GenericList updates={unsortedUpdates} animated items={unsorted} listId="people:unsorted">{shownItems => <div className="people-review-unsorted">{shownItems.map(face => {
        const rowKey = `face:${face.candidateId}`
        return <article key={rowKey} className="people-review-row people-review-single" aria-busy={review.isBusy(rowKey)}>
          <FaceStrip faces={[face]} title="Unsorted face" assetsById={assetsById} onOpen={open} />
          <div className="people-review-single-content">
            {review.isBusy(rowKey) && <small role="status">Saving…</small>}
            <FaceValidationDetails face={face} rowKey={rowKey} review={review} />
            <NameRowActions rowKey={rowKey} faces={[face]} canIgnore people={people} review={review} explainAssignment />
          </div>
        </article>
      })}</div>}</GenericList>
      </>}
    </section>}

    {(hasIgnored || ignoredUpdates.count > 0) && <section className="subsection-panel">
      <h4 className="tags-subhead people-review-collapsible-head"><button type="button" className="subsection-toggle people-review-toggle" aria-expanded={!ignoredCollapsed} onClick={toggleIgnored}><span className="people-review-caret" aria-hidden="true">▾</span>Ignored <span className="section-count">{ignoredGroups.length}</span></button></h4>
      {!ignoredCollapsed && <>
      <p className="people-review-ignored-help">Ignored groups are set aside for later. “Excluded from people” blocks a specific face from grouping; it does not delete the photo.</p>
      <GenericList updates={ignoredUpdates} animated items={ignoredGroups} listId="people:ignored">{shownItems => shownItems.map((group, index) => {
        const rowKey = `ignored:${group.groupId}`
        return <article key={rowKey} className="people-review-row people-review-ignored-group" aria-busy={review.isBusy(rowKey)}>
          <div className="people-review-row-head">
            <span className="people-review-title">Group {index + 1}</span>
            <small>{group.faces.length} {group.faces.length === 1 ? 'face' : 'faces'}</small>
            {review.isBusy(rowKey) && <small role="status">Saving…</small>}
            <HintChip hint={group.hint} onUse={name => applyHint(rowKey, name)} />
          </div>
          <IgnoredFaceGallery faces={group.faces} assetsById={assetsById} onOpen={open} review={review} />
          <NameRowActions rowKey={rowKey} faces={group.faces} canIgnore={false} people={people} review={review} showSelection />
        </article>
      })}</GenericList></>}
    </section>}

    <PhotoPopupDialog target={openPhoto} assetsById={assetsById} onClose={() => setOpenPhoto(null)} />
  </div>
}
