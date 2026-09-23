export function coalescedRead<T>(delayMs = 200) {
  let pending: Promise<T> | undefined
  let resolvePending: ((value: T) => void) | undefined
  let rejectPending: ((error: unknown) => void) | undefined
  let latest: (() => Promise<T>) | undefined
  let running = false
  let timer: ReturnType<typeof setTimeout> | undefined
  let generation = 0

  async function run() {
    timer = undefined
    const load = latest
    latest = undefined
    if (!load) return
    running = true
    const version = generation
    let value: T | undefined
    let error: unknown
    let failed = false
    try { value = await load() } catch (cause) { error = cause; failed = true }
    if (version !== generation) return
    running = false
    // Readers arriving during a request wait for one trailing refresh, so an older
    // response cannot overwrite a mutation that happened while it was in flight.
    if (latest) { timer = setTimeout(() => void run(), delayMs); return }
    const resolve = resolvePending
    const reject = rejectPending
    pending = undefined; resolvePending = undefined; rejectPending = undefined
    if (failed) reject?.(error)
    else resolve?.(value as T)
  }

  return {
    read(load: () => Promise<T>, refresh = true): Promise<T> {
      if (!running || refresh) latest = load
      pending ??= new Promise<T>((resolve, reject) => { resolvePending = resolve; rejectPending = reject })
      const result = pending
      if (!running && timer === undefined) timer = setTimeout(() => void run(), delayMs)
      return result
    },
    clear() {
      generation++
      if (timer !== undefined) clearTimeout(timer)
      timer = undefined
      latest = undefined
      running = false
      rejectPending?.(new Error('Session changed'))
      pending = undefined; resolvePending = undefined; rejectPending = undefined
    },
    get idle() { return !running && timer === undefined },
  }
}
