export function SectionReload({ changed, loading, error, reload }: {
  changed: boolean; loading?: boolean; error?: string | null; reload: () => void
}) {
  if (!changed && !error) return null
  return <div className="section-refresh">
    <span role="status">{error ?? 'Existing items have changed.'}</span>
    <button type="button" className="btn btn-sm" disabled={loading} onClick={reload}>
      {loading ? 'Loading…' : 'Reload'}
    </button>
  </div>
}
