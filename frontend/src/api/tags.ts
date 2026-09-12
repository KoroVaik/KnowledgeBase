import { apiFetch, readErrorMessage } from './http'

/** Mirrors TagResponse in backend Controllers/Tags. */
export interface Tag {
  id: string
  name: string
  /** The user has vouched for it. A pipeline-invented tag is `false` until reviewed. */
  confirmed: boolean
  /** Live Source notes carrying it. A synthesis needs at least two. */
  noteCount: number
  /** For an invented tag: the tag (confirmed, or itself still unconfirmed) the model judged
   *  closest in meaning. */
  suggestedMergeIntoId: string | null
  /** Ids of the tags this one is a child of (is-a). Manual/API-driven, not the model's call. */
  parentIds: string[]
  /** On either side of a pending AI placement guess - fetch `fetchTagParentSuggestions`. */
  hasPendingPlacementSuggestion: boolean
}

/** One tag named in a placement suggestion - just enough for the suggestion graph. */
export interface TagSuggestion {
  id: string
  name: string
}

/** Mirrors TagParentSuggestionsResponse: the two sides of the same underlying rows. */
export interface TagParentSuggestions {
  suggestedParents: TagSuggestion[]
  suggestedChildren: TagSuggestion[]
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

/** Queues a job re-running the closest-matching-tag suggestion over every unconfirmed tag,
 *  against the whole vocabulary (confirmed and other unconfirmed tags alike). */
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

/** Pending AI placement suggestions for one tag, both sides of the same rows. */
export async function fetchTagParentSuggestions(id: string): Promise<TagParentSuggestions> {
  const response = await apiFetch(`/api/tags/${encodeURIComponent(id)}/parent-suggestions`)

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not load placement suggestions'))
  }

  return (await response.json()) as TagParentSuggestions
}

/** Accepts the placement suggestion between two tags, in whichever direction it was proposed -
 *  creates the real parent link and removes the suggestion. */
export async function acceptTagParentSuggestion(id: string, otherId: string): Promise<void> {
  const response = await apiFetch(
    `/api/tags/${encodeURIComponent(id)}/parent-suggestions/${encodeURIComponent(otherId)}/accept`,
    { method: 'POST' },
  )

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not accept the suggestion'))
  }
}

/** Rejects the placement suggestion between two tags - kept as dismissed so it is not proposed
 *  again on the next suggest-hierarchy run. */
export async function rejectTagParentSuggestion(id: string, otherId: string): Promise<void> {
  const response = await apiFetch(
    `/api/tags/${encodeURIComponent(id)}/parent-suggestions/${encodeURIComponent(otherId)}/reject`,
    { method: 'POST' },
  )

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not reject the suggestion'))
  }
}

/** Queues a job finding a parent for every confirmed tag with none yet and no pending suggestion. */
export async function suggestTagHierarchy(): Promise<void> {
  const response = await apiFetch('/api/tags/suggest-hierarchy', { method: 'POST' })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not queue placement suggestions'))
  }
}
