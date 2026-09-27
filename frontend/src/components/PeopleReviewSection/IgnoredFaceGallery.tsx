import type { AssetSummary } from '../../api/assets'
import type { FaceBounds, FaceValidation, PeopleReviewFace } from '../../api/photoAnalysis'
import { FaceCropPreview } from '../FacePreview/FacePreview'
import { FaceValidationDetails } from './FaceValidationDetails'
import type { usePeopleReview } from './usePeopleReview'

const compactStatuses: Record<FaceValidation['status'], string> = {
  Pending: 'Pending', Eligible: 'Ready', NeedsReview: 'Review', Approved: 'Approved', Excluded: 'Excluded',
}

export function IgnoredFaceGallery({ faces, assetsById, onOpen, review }: {
  faces: PeopleReviewFace[]
  assetsById: Map<string, AssetSummary>
  onOpen: (title: string, assetId: string, faceBounds: FaceBounds) => void
  review: ReturnType<typeof usePeopleReview>
}) {
  return <div className="people-review-gallery">
    {faces.map((face, index) => {
      const status = face.validation?.status
      return <article key={face.candidateId} className="people-review-ignored-face" aria-label={`Face ${index + 1}`}>
        <div className="people-review-tile">
          <div className="people-review-tile-photo">
            <button type="button" className="people-review-crop" aria-label={`View full photo for face ${index + 1}`}
              onClick={() => onOpen('Ignored face', face.assetId, face.faceBounds)}>
              <FaceCropPreview asset={assetsById.get(face.assetId)} faceBounds={face.faceBounds} size={96} />
            </button>
            <label className="people-review-tile-select">
              <input type="checkbox" checked={review.isChecked(face.candidateId)} onChange={() => review.toggleFace(face.candidateId)}
                aria-label={`Select face ${index + 1} for person assignment`} />
            </label>
          </div>
          <span className="people-review-status" data-status={status ?? (face.needsReview ? 'NeedsReview' : 'Unknown')}>
            {status ? compactStatuses[status] : face.needsReview ? 'Review' : face.isPartial ? 'Photo edge' : 'Unchecked'}
          </span>
        </div>
        <div className="people-review-detail-panel">
          <strong>Face {index + 1}</strong>
          <FaceValidationDetails face={face} rowKey={`validation:${face.candidateId}`} review={review} />
          {!face.validation && <p>Validation information is not available for this face yet.</p>}
          <button type="button" className="btn btn-xs" onClick={() => onOpen('Ignored face', face.assetId, face.faceBounds)}>View full photo</button>
        </div>
      </article>
    })}
  </div>
}
