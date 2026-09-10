import { apiFetch, readErrorMessage } from './http'

/** Mirrors FeatureFlagsResponse in backend Controllers/Features. */
export interface FeatureFlags {
  uploadEnabled: boolean
  googleSignInEnabled: boolean
  /** Pipeline text-size limit in chars - a warning threshold. */
  maxSourceChars: number
  /** Hard cap upload-link refuses. */
  maxUploadBytes: number
}

export async function fetchFeatures(): Promise<FeatureFlags> {
  const response = await apiFetch('/api/features')

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not read the feature flags'))
  }

  return (await response.json()) as FeatureFlags
}
