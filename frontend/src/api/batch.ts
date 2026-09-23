import { apiFetch, readErrorMessage } from './http'
import { diagnosticId } from '../diagnostics/diagnostics'
import type { RequestContext } from '../diagnostics/diagnostics'

type Result<T> = { id: string; status: number; value: T | null; error: string | null }

export function batchRequest<TRequest, TValue>(path: string) {
  type Pending = { id: string; request: TRequest; context: RequestContext; resolve: (value: TValue) => void; reject: (error: unknown) => void }
  let queue: Pending[] = []
  let running = false
  let timer: ReturnType<typeof setTimeout> | undefined
  let generation = 0
  let active: Pending[] = []

  function schedule() {
    if (!running && timer === undefined && queue.length > 0) timer = setTimeout(() => void flush(), 20)
  }

  async function flush() {
    timer = undefined
    running = true
    const items = queue.splice(0, 50)
    active = items
    const version = generation
    try {
      const response = await apiFetch(path, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ items: items.map(({ id, request, context }) => ({ id, request, uploadBatchId: context.uploadBatchId, traceParent: context.traceId ? `00-${context.traceId}-${diagnosticId(8)}-01` : undefined })) }),
      }, { ...items[0].context, uploadId: undefined, assetId: undefined })
      if (!response.ok) throw new Error(await readErrorMessage(response, 'Batch request failed'))
      const results = await response.json() as Result<TValue>[]
      if (version !== generation) return
      const byId = new Map(results.map(result => [result.id, result]))
      for (const item of items) {
        const result = byId.get(item.id)
        if (result && result.status >= 200 && result.status < 300 && result.value !== null) item.resolve(result.value)
        else item.reject(new Error(result?.error ?? 'The item could not be processed'))
      }
    } catch (error) {
      for (const item of items) item.reject(error)
    } finally {
      if (version === generation) { active = []; running = false; schedule() }
    }
  }

  return {
    request(request: TRequest, context: RequestContext = {}): Promise<TValue> {
      const promise = new Promise<TValue>((resolve, reject) => queue.push({ id: context.uploadId ?? diagnosticId(), request, context, resolve, reject }))
      schedule()
      return promise
    },
    clear() {
      generation++
      if (timer !== undefined) clearTimeout(timer)
      timer = undefined
      for (const item of [...queue, ...active]) item.reject(new Error('Session changed'))
      queue = []; active = []; running = false
    },
  }
}
