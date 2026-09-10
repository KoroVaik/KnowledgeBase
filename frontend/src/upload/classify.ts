import { formatSize } from '../format'

/**
 * What is wrong with a dropped file, decided before anything leaves the browser.
 *
 * `blocked` is a wall: upload-link would answer 400, so there is no button to offer.
 * `warning` is a guess: the bytes upload fine and the file is stored, the pipeline just
 * probably will not turn it into a note. Only the user can say whether that is acceptable.
 */
export interface UploadProblem {
  kind: 'blocked' | 'warning'
  message: string
}

export interface UploadLimits {
  /** The hard cap upload-link refuses. 0 while /api/features has not answered yet. */
  maxUploadBytes: number
  /** The pipeline's character budget for text sources. 0 before the flags arrive. */
  maxSourceChars: number
}

const TEXT_FILE = /\.(txt|md|markdown)$/i

/**
 * Both limits are skipped while they are 0: the flags have not arrived, and refusing a file
 * against a threshold we do not know yet would be a guess in the one direction that costs
 * the user something.
 */
export function classify(file: File, limits: UploadLimits): UploadProblem | null {
  if (file.size === 0) {
    return { kind: 'blocked', message: 'The file is empty.' }
  }

  if (limits.maxUploadBytes > 0 && file.size > limits.maxUploadBytes) {
    return {
      kind: 'blocked',
      message: `Larger than the ${formatSize(limits.maxUploadBytes)} upload limit.`,
    }
  }

  // Bytes against a character budget, which is why this stays a "likely": the worker decides
  // for real, after it has extracted the text.
  if (limits.maxSourceChars > 0 && TEXT_FILE.test(file.name) && file.size > limits.maxSourceChars) {
    return {
      kind: 'warning',
      message:
        'This text file is large. It will upload and be stored, but the pipeline will likely '
        + 'skip it instead of making a note.',
    }
  }

  return null
}
