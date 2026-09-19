import type { Key } from 'react'
import { Dropdown } from '../Dropdown/Dropdown'
import './SearchPicker.css'

export interface SearchPickerSection<T> {
  heading?: string
  items: T[]
  /** Renders the items in the accent tone - a ranked suggestion that must read apart from plain matches. */
  accent?: boolean
}

export interface SearchPickerAdd {
  label: string
  busy?: boolean
  onAdd: () => void
}

/** A searchable picker over any kind of record. The caller owns the data: it turns the typed `text` into
 *  `sections` (and, when nothing fits, an `add` action), so the same input + panel serves a server-side tag
 *  search and a local people list alike.
 *
 *  `chip`: collapsed to a pill (`chipLabel` + a caret, styled by `chipClassName` / `chipLabelClassName`) until clicked; the panel
 *  then carries the input itself. Picking, Escape or a click outside closes the panel. */
export function SearchPicker<T>({
  text,
  onTextChange,
  onOpenChange,
  sections,
  add = null,
  error = null,
  emptyMessage,
  getKey,
  getLabel,
  onPick,
  disabled = false,
  placeholder,
  ariaLabel,
  className,
  inputId,
  chip = false,
  chipLabel,
  chipIconOnly = false,
  chipClassName,
  chipLabelClassName,
}: {
  text: string
  onTextChange: (value: string) => void
  onOpenChange?: (open: boolean) => void
  sections: SearchPickerSection<T>[]
  add?: SearchPickerAdd | null
  error?: string | null
  /** Shown when there is nothing to list and nothing to add. */
  emptyMessage?: string
  getKey: (item: T) => Key
  getLabel: (item: T) => string
  onPick: (item: T) => void
  disabled?: boolean
  placeholder?: string
  ariaLabel: string
  className?: string
  inputId?: string
  chip?: boolean
  chipLabel?: string
  chipIconOnly?: boolean
  chipClassName?: string
  chipLabelClassName?: string
}) {
  const visibleSections = sections.filter(section => section.items.length > 0)

  const options = (close: () => void) => <>
    {visibleSections.length === 0 && add === null && emptyMessage !== undefined && <p className="search-picker-empty">{emptyMessage}</p>}
    {visibleSections.map((section, index) => <div key={section.heading ?? index}>
      {section.heading !== undefined && <p className="search-picker-heading">{section.heading}</p>}
      <ul className={section.accent ? 'search-picker-results search-picker-accent' : 'search-picker-results'}>
        {section.items.map(item => <li key={getKey(item)}>
          <button type="button" onClick={() => { close(); onPick(item) }}>{getLabel(item)}</button>
        </li>)}
      </ul>
    </div>)}
    {add !== null && <button type="button" className="search-picker-add" disabled={add.busy} onClick={add.onAdd}>{add.label}</button>}
    {error !== null && <p className="search-picker-error">{error}</p>}
  </>

  const containerClassName = ['search-picker', className].filter(Boolean).join(' ')

  if (chip) {
    return <Dropdown
      className={`${containerClassName} search-picker-chip-anchor`}
      panelClassName="search-picker-panel"
      align="start"
      onOpenChange={onOpenChange}
      trigger={({ isOpen, toggle }) => <button
        type="button"
        className={['search-picker-chip', chipIconOnly && 'search-picker-chip-icon', chipClassName].filter(Boolean).join(' ')}
        aria-label={ariaLabel}
        aria-expanded={isOpen}
        disabled={disabled}
        onClick={toggle}
      >
        {!chipIconOnly && <span className={['search-picker-chip-label', chipLabelClassName].filter(Boolean).join(' ')}>{chipLabel}</span>}
        <span className="search-picker-chevron" aria-hidden="true">▾</span>
      </button>}
    >
      {close => <>
        <input type="text" className="field field-xs search-picker-input" value={text} placeholder={placeholder} disabled={disabled}
          aria-label={ariaLabel} autoFocus onChange={event => onTextChange(event.target.value)} />
        {options(close)}
      </>}
    </Dropdown>
  }

  return <Dropdown
    className={containerClassName}
    panelClassName="search-picker-panel"
    align="start"
    onOpenChange={onOpenChange}
    trigger={({ open }) => <input
      id={inputId}
      type="text"
      className="field field-xs search-picker-input"
      value={text}
      placeholder={placeholder}
      disabled={disabled}
      aria-label={ariaLabel}
      autoComplete="off"
      onFocus={open}
      onChange={event => { onTextChange(event.target.value); open() }}
    />}
  >
    {close => options(close)}
  </Dropdown>
}
