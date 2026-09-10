import { apiFetch, readErrorMessage } from './http'

/** Mirrors TagResponse in backend Controllers/Tags. */
export interface Tag {
  name: string
  /** The user has vouched for it. A pipeline-invented tag is `false` until reviewed. */
  confirmed: boolean
  /** Live Source notes carrying it. A synthesis needs at least two. */
  noteCount: number
}

/** Every tag, busiest first. */
export async function fetchTags(): Promise<Tag[]> {
  const response = await apiFetch('/api/tags')

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not load the tags'))
  }

  return (await response.json()) as Tag[]
}

/** Queues a job merging every Source note with this tag into one Synthesis note. */
export async function synthesiseTag(tag: string): Promise<void> {
  const response = await apiFetch('/api/synthesis/tag', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ tag }),
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not start the synthesis'))
  }
}

/** Queues a job merging every Synthesis note into the single Index note. */
export async function synthesiseIndex(): Promise<void> {
  const response = await apiFetch('/api/synthesis/index', { method: 'POST' })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not start the index'))
  }
}
