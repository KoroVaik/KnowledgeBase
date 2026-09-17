import { Dropdown } from '../Dropdown/Dropdown'
import './Select.css'

export interface SelectOption { id: string; label: string }

/** Single-choice dropdown on the shared Dropdown primitive — a native <select> renders
    its option list as an OS widget the page cannot style. */
export function Select({ value, onChange, options, placeholder, ariaLabel, align = 'start' }: {
  value: string
  onChange: (value: string) => void
  options: SelectOption[]
  /** Shown on the closed trigger while nothing is chosen and as the clearing row while something is. */
  placeholder?: string
  ariaLabel: string
  align?: 'start' | 'end'
}) {
  const selected = options.find(option => option.id === value)
  return <Dropdown align={align} panelClassName="select-panel" trigger={({ toggle, isOpen }) =>
    <button type="button" className="field field-xs select-trigger" aria-label={ariaLabel} aria-expanded={isOpen} onClick={toggle}>
      <span className={selected === undefined && placeholder !== undefined ? 'select-placeholder' : undefined}>{selected?.label ?? placeholder ?? ''}</span>
      <span className="select-caret" aria-hidden="true">▾</span>
    </button>}>
    {close => <div className="select-options">
      {placeholder !== undefined && selected !== undefined && <button type="button" className="select-option select-option-clear" onClick={() => { close(); onChange('') }}>{placeholder}</button>}
      {options.map(option => <button type="button" key={option.id} className="select-option" onClick={() => { close(); onChange(option.id) }}>{option.label}</button>)}
      {options.length === 0 && <span className="select-empty">Nothing to choose from yet</span>}
    </div>}
  </Dropdown>
}
