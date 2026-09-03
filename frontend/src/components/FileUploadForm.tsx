import { useRef, useState } from 'react'
import type { ChangeEvent, FormEvent } from 'react'
import { uploadNoteFile } from '../api/notes'
import type { UploadedAsset } from '../api/notes'

// A discriminated union instead of several booleans: the compiler then guarantees
// `asset` only exists on success and `message` only on error (like a sealed record
// hierarchy in C#).
type UploadState =
  | { status: 'idle' }
  | { status: 'uploading' }
  | { status: 'success'; asset: UploadedAsset }
  | { status: 'error'; message: string }

export function FileUploadForm() {
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [state, setState] = useState<UploadState>({ status: 'idle' })

  // <input type="file"> is uncontrolled - its value is owned by the DOM, not by React
  // state - so we need a ref to clear it after a successful upload.
  const inputRef = useRef<HTMLInputElement>(null)

  function handleFileChange(event: ChangeEvent<HTMLInputElement>) {
    setSelectedFile(event.target.files?.[0] ?? null)
    setState({ status: 'idle' })
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    // Without this the browser does a native form POST and reloads the page.
    event.preventDefault()

    if (selectedFile === null) {
      return
    }

    setState({ status: 'uploading' })

    try {
      const asset = await uploadNoteFile(selectedFile)
      setState({ status: 'success', asset })
      setSelectedFile(null)
      if (inputRef.current !== null) {
        inputRef.current.value = ''
      }
    } catch (error) {
      setState({
        status: 'error',
        message: error instanceof Error ? error.message : 'Unexpected error',
      })
    }
  }

  const isUploading = state.status === 'uploading'

  return (
    <section className="upload">
      <form className="upload-form" onSubmit={handleSubmit}>
        <label htmlFor="note-file">Document or image</label>
        <input
          id="note-file"
          ref={inputRef}
          type="file"
          onChange={handleFileChange}
          disabled={isUploading}
        />
        <button type="submit" disabled={selectedFile === null || isUploading}>
          {isUploading ? 'Uploading…' : 'Upload'}
        </button>
      </form>

      {state.status === 'error' && (
        <p className="upload-error" role="alert">
          {state.message}
        </p>
      )}

      {state.status === 'success' && (
        <div className="upload-result">
          <p>
            Stored <strong>{state.asset.originalFileName}</strong> as{' '}
            <code>{state.asset.storedFileName}</code> ({state.asset.sizeBytes} bytes)
          </p>
          <pre>{JSON.stringify(state.asset, null, 2)}</pre>
        </div>
      )}
    </section>
  )
}
