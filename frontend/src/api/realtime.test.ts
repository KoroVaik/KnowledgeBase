import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('../diagnostics/diagnostics', () => ({ record: vi.fn() }))

class FakeEventSource {
  static CONNECTING = 0
  static instances: FakeEventSource[] = []
  readyState = FakeEventSource.CONNECTING
  onopen: (() => void) | null = null
  onerror: (() => void) | null = null
  onmessage: ((event: MessageEvent<string>) => void) | null = null
  close = vi.fn(() => { this.readyState = 2 })

  constructor() { FakeEventSource.instances.push(this) }

  open() {
    this.readyState = 1
    this.onopen?.()
  }
}

let tab: EventTarget & { visibilityState: string }
let activity: EventTarget

beforeEach(() => {
  vi.resetModules()
  vi.useFakeTimers()
  FakeEventSource.instances = []
  tab = Object.assign(new EventTarget(), { visibilityState: 'visible' })
  activity = new EventTarget()
  vi.stubGlobal('document', tab)
  vi.stubGlobal('window', Object.assign(activity, { setTimeout, clearTimeout }))
  vi.stubGlobal('EventSource', FakeEventSource)
})

afterEach(() => {
  vi.clearAllTimers()
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

function latestSource() { return FakeEventSource.instances.at(-1)! }

function setVisibility(value: string) {
  tab.visibilityState = value
  tab.dispatchEvent(new Event('visibilitychange'))
}

async function connectedClient() {
  const client = await import('./realtime')
  const changes = vi.fn()
  const connection = vi.fn()
  client.subscribeToConnection(connection)
  client.subscribeToChanges('assets', changes)
  latestSource().open()
  return { client, changes, connection }
}

describe('SSE recovery', () => {
  it.each(['hidden', 'idle'])('keeps a slow connection alive after a %s pause', async (pause) => {
    const { changes, connection } = await connectedClient()
    const initial = latestSource()
    if (pause === 'hidden') {
      setVisibility('hidden')
      vi.advanceTimersByTime(30_000)
      expect(initial.close).toHaveBeenCalledOnce()
      setVisibility('visible')
    } else {
      vi.advanceTimersByTime(15 * 60_000)
      expect(initial.close).toHaveBeenCalledOnce()
      activity.dispatchEvent(new Event('pointerdown'))
    }

    const resumed = latestSource()
    vi.advanceTimersByTime(1_500)
    expect(connection).toHaveBeenLastCalledWith({ status: 'offline', reconnectAt: null })
    vi.advanceTimersByTime(20_000)
    expect(resumed.close).not.toHaveBeenCalled()
    expect(FakeEventSource.instances).toHaveLength(2)

    resumed.open()
    expect(connection).toHaveBeenLastCalledWith({ status: 'online', reconnectAt: null })
    expect(changes).toHaveBeenCalledExactlyOnceWith(null)
  })

  it('lets a manual reconnect finish slowly and ignores callbacks from the replaced stream', async () => {
    const { client, changes, connection } = await connectedClient()
    latestSource().onerror?.()
    vi.advanceTimersByTime(2_000)
    const replaced = latestSource()
    vi.advanceTimersByTime(1_500)

    client.reconnectNow()
    const manual = latestSource()
    expect(manual).not.toBe(replaced)
    expect(replaced.close).toHaveBeenCalledOnce()
    replaced.open()
    replaced.onerror?.()
    expect(changes).not.toHaveBeenCalled()

    vi.advanceTimersByTime(20_000)
    expect(manual.close).not.toHaveBeenCalled()
    expect(latestSource()).toBe(manual)
    manual.open()
    expect(connection).toHaveBeenLastCalledWith({ status: 'online', reconnectAt: null })
    expect(changes).toHaveBeenCalledExactlyOnceWith(null)
  })

  it('retries actual failures with backoff and allows the button to skip the wait', async () => {
    const { client, connection } = await connectedClient()
    latestSource().onerror?.()
    expect(connection).toHaveBeenLastCalledWith({ status: 'offline', reconnectAt: Date.now() + 2_000 })
    vi.advanceTimersByTime(1_999)
    expect(FakeEventSource.instances).toHaveLength(1)
    vi.advanceTimersByTime(1)
    expect(FakeEventSource.instances).toHaveLength(2)
    latestSource().onerror?.()
    expect(connection).toHaveBeenLastCalledWith({ status: 'offline', reconnectAt: Date.now() + 4_000 })

    client.reconnectNow()
    expect(FakeEventSource.instances).toHaveLength(3)
    latestSource().open()
    vi.advanceTimersByTime(4_000)
    expect(FakeEventSource.instances).toHaveLength(3)
    expect(connection).toHaveBeenLastCalledWith({ status: 'online', reconnectAt: null })
  })
})
