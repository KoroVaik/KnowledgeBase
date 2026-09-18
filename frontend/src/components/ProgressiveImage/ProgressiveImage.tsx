import type { CSSProperties } from 'react'
import { useProgressiveImage } from './useProgressiveImage'
import './ProgressiveImage.css'

interface ProgressiveImageProps {
  /** Signed bucket URL; null keeps the placeholder up (the link itself is still being fetched). */
  url: string | null
  alt: string
  /** Applied to the placeholder box and the finished image alike, so caller sizing rules fit both. */
  className?: string
  /** The percent counter only fits a large box - small thumbnails carry the shimmer alone. */
  showPercent?: boolean
  ariaHidden?: boolean
  imgStyle?: CSSProperties
  /** Fires once the image is decoded, with its natural size (face frames need it). */
  onImageLoad?: (image: HTMLImageElement) => void
}

/** An <img> that downloads its bytes through the app so the placeholder can show real
    progress: a shimmering grey gradient - with the percent in the middle for large images -
    until every byte is in, then the finished picture. A plain <img> paints progressively,
    showing the half-loaded file itself. */
export function ProgressiveImage({
  url,
  alt,
  className,
  showPercent = false,
  ariaHidden,
  imgStyle,
  onImageLoad,
}: ProgressiveImageProps) {
  const { percent, imageUrl, failed } = useProgressiveImage(url)
  const classes = className === undefined ? 'progressive-image' : `progressive-image ${className}`

  if (imageUrl === null) {
    return (
      <div className={`progressive-image-loading ${classes}`} aria-hidden={ariaHidden}>
        {failed ? (
          <span className="progressive-image-failed">Couldn’t load</span>
        ) : (
          showPercent && (
            <span className="progressive-image-percent">{Math.min(100, Math.round(percent * 100))}%</span>
          )
        )}
      </div>
    )
  }

  return (
    <img
      className={classes}
      src={imageUrl}
      alt={ariaHidden === true ? '' : alt}
      aria-hidden={ariaHidden}
      style={imgStyle}
      onLoad={(event) => onImageLoad?.(event.currentTarget)}
    />
  )
}
