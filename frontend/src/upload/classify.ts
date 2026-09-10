import { formatSize } from '../format'

// `blocked` = upload-link would answer 400, no button to offer. `warning` = uploads and is
// stored, the pipeline just probably will not make a note; the user decides.
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

// A limit of 0 means the flags have not arrived - skip that check rather than guess.
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

  // Bytes against a character budget - only a "likely"; the worker decides after extraction.
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
