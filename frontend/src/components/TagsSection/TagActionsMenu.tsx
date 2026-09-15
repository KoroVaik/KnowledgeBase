import { Dropdown } from '../Dropdown/Dropdown'

export function TagActionsMenu({
  disabled,
  queued,
  canSynthesise,
  onSynthesise,
  onDelete,
}: {
  disabled: boolean
  queued: boolean
  canSynthesise: boolean
  onSynthesise: () => void
  onDelete: () => void
}) {
  return (
    <Dropdown
      className="tag-actions-menu"
      panelClassName="tag-actions-menu-panel"
      trigger={({ isOpen, toggle }) => (
        <button
          type="button"
          className="btn btn-xs tag-actions-menu-toggle"
          aria-label="More tag actions"
          aria-expanded={isOpen}
          onClick={toggle}
          disabled={disabled}
        >
          <span aria-hidden="true">⋯</span>
        </button>
      )}
    >
      {(close) => (
        <>
          {canSynthesise && (
            <button
              type="button"
              className="tag-actions-menu-item"
              onClick={() => {
                close()
                onSynthesise()
              }}
              disabled={disabled}
            >
              {queued ? 'Queued' : 'Synthesise'}
            </button>
          )}

          <button
            type="button"
            className="tag-actions-menu-item tag-actions-menu-delete"
            onClick={() => {
              close()
              onDelete()
            }}
            disabled={disabled}
          >
            Delete
          </button>
        </>
      )}
    </Dropdown>
  )
}
