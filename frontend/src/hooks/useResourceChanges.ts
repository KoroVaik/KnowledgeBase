import { clearAssetCaches } from '../api/assets'
import { useEffect, useRef } from 'react'
import { record, withRequestContext } from '../diagnostics/diagnostics'
import { subscribeToChanges } from '../api/realtime'

/** Calls `onChange` when the server reports this collection changed, and after a reconnect.
 *  An event says what changed, not to what - so re-read the collection. */
export function useResourceChanges(resource: string, onChange: () => void) {
  // In a ref so a handler rebuilt each render does not re-open the stream.
  const handler = useRef(onChange)

  useEffect(() => {
    handler.current = onChange
  })

  useEffect(() => subscribeToChanges(resource, (change) => {
    if (change?.resource === 'assets' && change.action === 'deleted' && change.id) clearAssetCaches(change.id)
    const traceId = change?.traceParent?.match(/^00-([a-f0-9]{32})-[a-f0-9]{16}-[a-f0-9]{2}$/)?.[1]
    const context = { uploadId: change?.context?.uploadId, uploadBatchId: change?.context?.uploadBatchId,
      jobId: change?.context?.jobId, traceId, causationId: change?.eventId, trigger: change === null ? 'reconnect' : 'sse' }
    record('resource.reload', { resource, trigger: context.trigger, causationId: context.causationId }, 'debug')
    withRequestContext(context, () => handler.current())
  }), [resource])
}
