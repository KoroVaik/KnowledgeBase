import { useEffect, useRef } from 'react'
import { isBusy, needsAttention } from '../../upload/useUploadQueue'
import type { QueuedItem } from '../../upload/useUploadQueue'

/** State behind UploadQueueDialog: the dialog's own open/close (an imperative DOM call, not a
 *  prop) and the counts/filters the header and footer buttons need. */
export function useUploadQueueDialog(open: boolean, items: QueuedItem[]) {
  const dialogRef = useRef<HTMLDialogElement>(null)

  // Modality only exists as an imperative call - React has to reach into the DOM here.
  useEffect(() => {
    const dialog = dialogRef.current

    if (dialog === null) {
      return
    }

    if (open && !dialog.open) {
      dialog.showModal()
    } else if (!open && dialog.open) {
      dialog.close()
    }
  }, [open])

  return {
    dialogRef,
    busy: isBusy(items),
    warnings: items.filter((item) => item.state.status === 'needs-decision'),
    attention: items.filter(needsAttention),
    done: items.filter((item) => item.state.status === 'done').length,
  }
}
