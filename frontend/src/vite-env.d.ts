/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Base URL of the ASP.NET Core API. Defaults to the `http` launch profile. */
  readonly VITE_API_BASE_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
