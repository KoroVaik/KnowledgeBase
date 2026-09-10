import { useRef, useState } from 'react'
import type { ChangeEvent, FormEvent } from 'react'
import { uploadAsset } from '../api/assets'

type UploadState =
  | { status: 'idle' }
  // `progress` is null while the two small API calls run: they carry no measurable body,
  // so the bar has nothing to show and stays indeterminate until the PUT starts reporting.
  | { status: 'uploading'; progress: number | null }
  | { status: 'error'; message: string }

interface FileUploadFormProps {
  onUploaded: () => void
  uploadEnabled: boolean
  /** From /api/features: text files larger than this are likely skipped by the pipeline. */
  maxSourceChars: number
}

const TEXT_FILE = /\.(txt|md|markdown)$/i

export function FileUploadForm({ onUploaded, uploadEnabled, maxSourceChars }: FileUploadFormProps) {
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [state, setState] = useState<UploadState>({ status: 'idle' })

  // <input type="file"> is uncontrolled: its value is owned by the DOM, so clearing it
  // after a successful upload needs a ref.
  const inputRef = useRef<HTMLInputElement>(null)

  function handleFileChange(event: ChangeEvent<HTMLInputElement>) {
    setSelectedFile(event.target.files?.[0] ?? null)
    setState({ status: 'idle' })
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (selectedFile === null) {
      return
    }

    setState({ status: 'uploading', progress: null })

    try {
      await uploadAsset(selectedFile, (fraction) => {
        setState({ status: 'uploading', progress: fraction })
      })
      setState({ status: 'idle' })
      setSelectedFile(null)
      if (inputRef.current !== null) {
        inputRef.current.value = ''
      }
      // Success is not reported here: the new row showing up in the list below says it.
      onUploaded()
    } catch (error) {
      setState({
        status: 'error',
        message: error instanceof Error ? error.message : 'Unexpected error',
      })
    }
  }

  // A heads-up, not a block: the file still uploads and is stored, it just probably will not
  // become a note. Byte size against a character limit, so it stays a "likely".
  const likelyTooLargeForPipeline =
    selectedFile !== null &&
    maxSourceChars > 0 &&
    TEXT_FILE.test(selectedFile.name) &&
    selectedFile.size > maxSourceChars

  const isUploading = state.status === 'uploading'
  const isBlocked = isUploading || !uploadEnabled
  const progress = state.status === 'uploading' ? state.progress : null
  const uploadLabel = progress === null ? 'Uploading…' : `Uploading… ${Math.round(progress * 100)}%`

  return (
    <section className="upload">
      {!uploadEnabled && <p className="upload-disabled">Uploading is turned off.</p>}

      <form className="upload-form" onSubmit={handleSubmit}>
        <label htmlFor="note-file">Document or image</label>
        <input
          id="note-file"
          ref={inputRef}
          type="file"
          onChange={handleFileChange}
          disabled={isBlocked}
        />
        <button type="submit" disabled={selectedFile === null || isBlocked}>
          {isUploading ? uploadLabel : 'Upload'}
        </button>
      </form>

      {likelyTooLargeForPipeline && !isUploading && (
        <p className="upload-hint" role="status">
          This text file is large — it will upload, but the pipeline will likely skip it
          instead of making a note. Consider splitting it.
        </p>
      )}

      {isUploading && (
        // No `value` while the fraction is unknown: that is what puts a native <progress>
        // into its indeterminate look, instead of showing a hard zero.
        <progress
          className="upload-progress"
          max={1}
          value={progress ?? undefined}
          aria-label="Upload progress"
        />
      )}

      {state.status === 'error' && (
        <p className="upload-error" role="alert">
          {state.message}
        </p>
      )}
    </section>
  )
}
