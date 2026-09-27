import type { PeopleReviewFace } from '../../api/photoAnalysis'
import { InfoHint } from '../InfoHint/InfoHint'
import type { usePeopleReview } from './usePeopleReview'

const statuses = { Pending: 'Awaiting validation', Eligible: 'Human face', NeedsReview: 'Needs review', Approved: 'Manually approved', Excluded: 'Excluded from people' }
const statusHints: Record<NonNullable<PeopleReviewFace['validation']>['status'], string> = {
  Pending: 'Waiting for automatic validation by the worker. This face stays out of automatic grouping until validation passes. Choosing a person also confirms the face. Allow automatic grouping confirms it without assigning a person. If validation failed, use Retry validation.',
  Eligible: 'Automatic validation classified this as a human face, with no blocking detector warning. This face can be grouped with other faces. Validation does not identify the person; selecting a name is a separate step.',
  NeedsReview: 'A possible face was detected in this photo, but automatic checks could not confirm it as a human face. It needs your review before automatic grouping.',
  Approved: 'You manually allowed this face for people grouping, overriding automatic validation. This does not assign a person; selecting a name is a separate step.',
  Excluded: 'You manually excluded this face from people grouping. The original photo is not deleted. Choosing a person confirms the face and overrides this exclusion. Allow automatic grouping overrides exclusion without assigning a person.',
}
const reasons: Record<string, string> = {
  ImageEdge: 'Touches the photo edge (warning only)', DetectorUnconfirmed: 'Not confirmed by the second detector',
  SmallCrop: 'Small original crop', LowSharpness: 'Low sharpness', AnimalFace: 'Possible animal',
  StatueOrArtwork: 'Possible statue or artwork', NotFace: 'No face detected by the check', Uncertain: 'Uncertain subject',
}

export function FaceValidationDetails({ face, rowKey, review }: {
  face: PeopleReviewFace
  rowKey: string
  review: ReturnType<typeof usePeopleReview>
}) {
  const validation = face.validation
  if (!validation) return null
  const busy = review.isBusy(rowKey)
  return <div className="people-review-validation">
    <div>
      <strong>{statuses[validation.status]}</strong>
      <InfoHint id={`face-validation-${face.faceOccurrenceId}`} label={`About ${statuses[validation.status]}`} description={statusHints[validation.status]} />
    </div>
    {validation.reasons.length > 0 && <p>{validation.reasons.map(reason => reasons[reason] ?? reason).join(' · ')}</p>}
    {validation.error && <p className="notes-error">{validation.error}</p>}
    {(validation.evidence || validation.minSidePixels !== null) && <details>
      <summary>Check details</summary>
      {validation.evidence && <p>AI observation: {validation.evidence}</p>}
      {validation.minSidePixels !== null && <p>Original crop: {validation.minSidePixels} px on its shorter side.
        {validation.sharpness112 !== null && <> Sharpness: {validation.sharpness112.toFixed(1)}.</>}</p>}
      <p>Photo edge, size and sharpness are warnings. They do not automatically reject a face.</p>
    </details>}
    <div className="people-review-actions">
      {!validation.canUseForPeople && <button type="button" className="btn btn-xs" disabled={busy}
        title="Confirm this is a human face so it can be grouped with similar faces." onClick={() => review.validate(rowKey, face, 'Approved')}>Allow automatic grouping</button>}
      {validation.status !== 'Excluded' && <button type="button" className="btn btn-xs" disabled={busy}
        title="Keep this face out of automatic grouping. The photo stays." onClick={() => review.validate(rowKey, face, 'Excluded')}>Exclude from people</button>}
      {validation.canRetry && <button type="button" className="btn btn-xs" disabled={busy}
        title="Run the automatic face check again." onClick={() => review.retryValidation(rowKey, face)}>Retry validation</button>}
    </div>
  </div>
}
