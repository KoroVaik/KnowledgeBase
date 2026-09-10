import { apiFetch, readErrorMessage } from './http'

/** Mirrors FeatureFlagsResponse in backend/Controllers/Features. */
export interface FeatureFlags {
  uploadEnabled: boolean
  downloadEnabled: boolean
  googleSignInEnabled: boolean
  /** The pipeline's text-size limit in characters: a warning threshold, not an upload cap. */
  maxSourceChars: number
}

export async function fetchFeatures(): Promise<FeatureFlags> {
  const response = await apiFetch('/api/features')

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not read the feature flags'))
  }

  return (await response.json()) as FeatureFlags
}
