import { useState } from 'react'
import { Dropdown } from '../Dropdown/Dropdown'
import './MultiSelect.css'

export interface MultiSelectOption { id: string; label: string }

export function SelectionHead({ count, onClear }: { count: number; onClear: () => void }) {
  if (count === 0) return null
  return <div className="multiselect-panel-head">
    <span>{count} selected</span>
    <button type="button" onClick={onClear}>Clear</button>
  </div>
}

/** Text options as removable chips plus a searchable "add" dropdown; the dropdown stays open while picking. */
export function MultiSelect({ options, selected, onChange, placeholder, ariaLabel }: {
  options: MultiSelectOption[]
  selected: string[]
  onChange: (next: string[]) => void
  placeholder: string
  ariaLabel: string
}) {
  const [query, setQuery] = useState('')
  const filter = query.trim().toLowerCase()
  const selectedOptions = selected
    .map(id => options.find(option => option.id === id))
    .filter((option): option is MultiSelectOption => option !== undefined)
  const available = options.filter(option => !selected.includes(option.id) && (filter === '' || option.label.toLowerCase().includes(filter)))
  const add = (id: string) => { onChange([...selected, id]); setQuery('') }
  return <div className="multiselect">
    {selectedOptions.length > 0 && <div className="multiselect-chips">
      {selectedOptions.map(option => <span key={option.id} className="multiselect-chip">
        {option.label}
        <button type="button" className="multiselect-chip-remove" aria-label={`Remove ${option.label}`} onClick={() => onChange(selected.filter(value => value !== option.id))}>×</button>
      </span>)}
    </div>}
    <Dropdown align="start" panelClassName="multiselect-panel-root" trigger={({ toggle, isOpen }) =>
      <button type="button" className="field field-xs multiselect-trigger" aria-label={ariaLabel} aria-expanded={isOpen} onClick={toggle}>{placeholder}</button>}>
      {() => <div className="multiselect-picker">
        <input className="field field-xs multiselect-search" placeholder="Search…" value={query} onChange={event => setQuery(event.target.value)} autoFocus />
        <SelectionHead count={selected.length} onClear={() => onChange([])} />
        <div className="multiselect-options">
          {available.map(option => <button type="button" key={option.id} className="multiselect-option" onClick={() => add(option.id)}>{option.label}</button>)}
          {available.length === 0 && <span className="multiselect-empty">No matches</span>}
        </div>
      </div>}
    </Dropdown>
  </div>
}
