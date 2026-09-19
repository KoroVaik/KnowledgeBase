import type { Person } from '../../api/photoAnalysis'
import { SearchPicker } from '../SearchPicker/SearchPicker'

const MAX_SUGGESTIONS = 10

/** SearchPicker over the archive's people: existing names filtered by the typed text, and "Add new name" for
 *  a name nobody has yet. */
export function PersonNamePicker({ inputId, people, value, disabled, onChange, onPickPerson, onAddName }: {
  inputId: string
  people: Person[]
  value: string
  disabled: boolean
  onChange: (value: string) => void
  onPickPerson: (person: Person) => void
  onAddName: (name: string) => void
}) {
  const name = value.trim()
  const query = name.toLowerCase()
  const matches = people
    .filter(person => person.name.toLowerCase().includes(query))
    .sort((left, right) => left.name.localeCompare(right.name))
    .slice(0, MAX_SUGGESTIONS)
  const canAdd = query !== '' && !people.some(person => person.name.trim().toLowerCase() === query)

  return <SearchPicker
    className="person-picker"
    inputId={inputId}
    text={value}
    onTextChange={onChange}
    sections={[{ items: matches }]}
    add={canAdd ? { label: `Add new name "${name}"`, onAdd: () => onAddName(name) } : null}
    getKey={person => person.id}
    getLabel={person => person.name}
    onPick={onPickPerson}
    disabled={disabled}
    placeholder="Select person name"
    ariaLabel="Select person name"
  />
}
