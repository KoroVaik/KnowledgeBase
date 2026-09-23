import { useEffect, useRef, useState, type ReactNode } from 'react'
import type { FaceBounds } from '../../api/photoAnalysis'
import { useAssetPreview } from '../../hooks/useAssetPreview'
import type { AssetSummary } from '../../api/assets'
import { ProgressiveImage } from '../ProgressiveImage/ProgressiveImage'
import './FacePreview.css'

export interface FaceOverlay {
  id: string
  bounds: FaceBounds
  landmarks: { x: number; y: number }[]
  label: string
  labelParts?: { text: string; color: string }[]
  isFace?: boolean | null
}

/** The full photo, optionally with a box drawn on one detected face — shared by the review preview and every Known * popup.
    The outer frame box letterboxes the photo: the inner box always carries the source aspect ratio,
    so the %-positioned face frame maps onto the visible image rather than the bars around it, and a
    square frame holds its shape instead of letting a portrait photo stretch the review row. */
export function FullPhotoPreview({ asset, faceBounds, label, labelContent, square = false, overlays, highlightedOverlayId, onOverlayHover, maxHeightVh = 68 }: { asset: AssetSummary | undefined; faceBounds?: FaceBounds; label?: string; labelContent?: ReactNode; square?: boolean; overlays?: FaceOverlay[]; highlightedOverlayId?: string; onOverlayHover?: (id: string | null) => void; maxHeightVh?: number }) {
  const { url, unavailable } = useAssetPreview(asset?.storedFileName, asset?.id, 'FacePreview')
  const [sourceSize, setSourceSize] = useState<{ width: number; height: number } | null>(null)
  const [boxWidth, setBoxWidth] = useState<number | null>(null)
  const boxRef = useRef<HTMLDivElement>(null)
  const previewRef = useRef<HTMLDivElement>(null)
  const labelRef = useRef<HTMLSpanElement>(null)
  const [labelFit, setLabelFit] = useState<{ scale: number; clampPx: number } | null>(null)


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
  }, [square, url, maxHeightVh])

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
      const width = Math.min(boxWidth, window.innerHeight * maxHeightVh / 100 * sourceSize.width / sourceSize.height)
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
      {sourceSize && overlays && <svg className="face-overlays" viewBox={`0 0 ${sourceSize.width} ${sourceSize.height}`} aria-label="Detected face boxes and landmarks">
        {overlays.map(face => <g key={face.id} onMouseEnter={() => onOverlayHover?.(face.id)} onMouseLeave={() => onOverlayHover?.(null)}
          className={`${face.isFace === false ? 'face-overlay-rejected' : face.isFace === true ? 'face-overlay-confirmed' : ''}${highlightedOverlayId === face.id ? ' face-overlay-highlighted' : ''}${onOverlayHover ? ' face-overlay-interactive' : ''}`}>
          <title>{face.label}</title>
          <rect x={face.bounds.x} y={face.bounds.y} width={Math.max(0, face.bounds.width)} height={Math.max(0, face.bounds.height)} />
          <text x={Math.max(0, Math.min(sourceSize.width - 20, face.bounds.x))} y={Math.max(14, face.bounds.y - 4)} fontSize={Math.max(12, sourceSize.width / 45)}>{face.labelParts?.map((part, index) => <tspan key={index} fill={part.color}>{part.text}</tspan>) ?? face.label}</text>
          {face.landmarks.map((point, index) => <circle key={index} cx={point.x} cy={point.y} r={Math.max(1.5, sourceSize.width / 350)} />)}
        </g>)}
      </svg>}
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
  const faceWidth = Math.max(1, Math.min(bounds.width, source.width))
  const faceHeight = Math.max(1, Math.min(bounds.height, source.height))
  const side = Math.min(Math.max(faceWidth, faceHeight) * 1.3, source.width, source.height)
  const centerX = Math.min(Math.max(bounds.x + bounds.width / 2, side / 2), source.width - side / 2)
  const centerY = Math.min(Math.max(bounds.y + bounds.height / 2, side / 2), source.height - side / 2)
  const scale = size / side
  return { width: source.width * scale, height: source.height * scale, left: -(centerX - side / 2) * scale, top: -(centerY - side / 2) * scale }
}

/** A tight square crop around one detected face, loaded from the full photo (no server-side thumbnail). */
export function FaceCropPreview({ asset, faceBounds, size = 144 }: { asset: AssetSummary | undefined; faceBounds: FaceBounds; size?: number }) {
  const { url, unavailable } = useAssetPreview(asset?.storedFileName, asset?.id, 'FacePreview')
  const [sourceSize, setSourceSize] = useState<{ width: number; height: number } | null>(null)


  const crop = url !== null && sourceSize !== null ? faceCrop(faceBounds, sourceSize, size) : null
  return <div className="face-crop-preview" style={{ width: size, height: size }}>
    {url === null && unavailable
      ? <span>Unavailable</span>
      : <ProgressiveImage
          url={url}
          alt=""
          ariaHidden
          imgStyle={crop ?? undefined}
          onImageLoad={(image) => setSourceSize({ width: image.naturalWidth, height: image.naturalHeight })}
        />}
  </div>
}

/** One opened photo: whose record it belongs to, which photo, and what revoking it means. */
export interface PhotoPopupTarget { title: string; assetId: string; faceBounds?: FaceBounds; overlays?: FaceOverlay[]; onRevoke?: () => void }

export function PhotoPopupDialog({ target, assetsById, onClose }: { target: PhotoPopupTarget | null; assetsById: Map<string, AssetSummary>; onClose: () => void }) {
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
      <h3 id="face-popup-title">{target.title}{asset && <span className="face-popup-filename"> ({asset.originalFileName})</span>}</h3>
      <FullPhotoPreview key={target.assetId} asset={asset} faceBounds={target.faceBounds} overlays={target.overlays} />
      <div className="confirm-actions">{target.onRevoke !== undefined && <button type="button" className="btn btn-lg" onClick={() => { target.onRevoke?.(); onClose() }}>Revoke</button>}<button type="button" className="btn btn-lg" onClick={onClose}>Close</button></div>
    </>}
  </dialog>
}
