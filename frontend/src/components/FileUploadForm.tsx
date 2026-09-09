import { useRef, useState } from 'react'
import type { ChangeEvent, FormEvent } from 'react'
import { uploadAsset } from '../api/assets'

type UploadState =
  | { status: 'idle' }
  | { status: 'uploading' }
  | { status: 'error'; message: string }

interface FileUploadFormProps {
  onUploaded: () => void
  uploadEnabled: boolean
}

export function FileUploadForm({ onUploaded, uploadEnabled }: FileUploadFormProps) {
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

    setState({ status: 'uploading' })

    try {
      await uploadAsset(selectedFile)
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

  const isUploading = state.status === 'uploading'
  const isBlocked = isUploading || !uploadEnabled

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
          {isUploading ? 'Uploading…' : 'Upload'}
        </button>
      </form>

      {state.status === 'error' && (
        <p className="upload-error" role="alert">
          {state.message}
        </p>
      )}
    </section>
  )
}
