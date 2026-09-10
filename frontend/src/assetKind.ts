import type { AssetSummary } from './api/assets'

type FileFacts = Pick<AssetSummary, 'contentType' | 'originalFileName'>

const PROCESSABLE_EXT = /\.(txt|md|markdown|jpe?g|png|webp|gif|pdf)$/i
const IMAGE_EXT = /\.(jpe?g|png|webp|gif)$/i

/** Mirrors backend ProcessableContent.Classify: whether the pipeline takes this file type. */
export function isProcessable({ contentType, originalFileName }: FileFacts): boolean {
  const type = contentType.toLowerCase()

  return (
    type === 'application/pdf' ||
    type.startsWith('text/') ||
    type.startsWith('image/') ||
    PROCESSABLE_EXT.test(originalFileName)
  )
}

/** An image the file panel can show inline with an <img>. */
export function isImage({ contentType, originalFileName }: FileFacts): boolean {
  return contentType.toLowerCase().startsWith('image/') || IMAGE_EXT.test(originalFileName)
}
