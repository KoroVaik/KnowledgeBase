import { apiFetch, readErrorMessage } from './http'

/** Mirrors TagResponse in backend Controllers/Tags. */
export interface Tag {
  id: string
  name: string
  /** The user has vouched for it. A pipeline-invented tag is `false` until reviewed. */
  confirmed: boolean
  /** Live Source notes carrying it. A synthesis needs at least two. */
  noteCount: number
  /** For an invented tag: the confirmed tag the model judged closest in meaning. */
  suggestedMergeIntoId: string | null
  /** Ids of the tags this one is a child of (is-a). Manual/API-driven, not the model's call. */
  parentIds: string[]
}

export interface TagSearch {
  /** Filters to matching tags, closest match first. Omit for every tag, busiest first. */
  query?: string
  /** Drops one tag (typically the one being merged) from the results. */
  excludeId?: string
}

/** Tags matching the search, ranked on the backend - see TagsController.List. */
export async function fetchTags(search?: TagSearch): Promise<Tag[]> {
  const params = new URLSearchParams()

  if (search?.query) {
    params.set('query', search.query)
  }
  if (search?.excludeId) {
    params.set('excludeId', search.excludeId)
  }

  const qs = params.toString()
  const response = await apiFetch(qs.length > 0 ? `/api/tags?${qs}` : '/api/tags')

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not load the tags'))
  }

  return (await response.json()) as Tag[]
}

/** Adds a tag by hand, confirmed - the picker's "Add new tag" escape hatch. */
export async function createTag(name: string): Promise<Tag> {
  const response = await apiFetch('/api/tags', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ name }),
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not add the tag'))
  }

  return (await response.json()) as Tag
}

export async function confirmTag(id: string): Promise<void> {
  const response = await apiFetch(`/api/tags/${encodeURIComponent(id)}/confirm`, { method: 'POST' })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not confirm the tag'))
  }
}

/** Moves every note from tag `id` to `intoId`, deletes `id` and bins its synthesis note. */
export async function mergeTag(id: string, intoId: string): Promise<void> {
  const response = await apiFetch(`/api/tags/${encodeURIComponent(id)}/merge`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ intoId }),
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not merge the tag'))
  }
}

/** Takes the tag off every note and bins its synthesis note. */
export async function deleteTag(id: string): Promise<void> {
  const response = await apiFetch(`/api/tags/${encodeURIComponent(id)}`, { method: 'DELETE' })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not delete the tag'))
  }
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

/** Queues a job re-running the closest-confirmed-tag suggestion over every unconfirmed tag. */
export async function suggestTagMerges(): Promise<void> {
  const response = await apiFetch('/api/tags/suggest-merges', { method: 'POST' })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not queue tag suggestions'))
  }
}

/** Adds `parentId` as a parent (is-a) of tag `id`. */
export async function addTagParent(id: string, parentId: string): Promise<void> {
  const response = await apiFetch(`/api/tags/${encodeURIComponent(id)}/parents`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ parentId }),
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not add the parent tag'))
  }
}

/** Removes the parent (is-a) link between tags `id` and `parentId`. */
export async function removeTagParent(id: string, parentId: string): Promise<void> {
  const response = await apiFetch(
    `/api/tags/${encodeURIComponent(id)}/parents/${encodeURIComponent(parentId)}`,
    { method: 'DELETE' },
  )

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not remove the parent tag'))
  }
}
