import { useEffect, useState } from 'react'
import { fetchDownloadUrl } from '../../api/assets'
import { Dropdown } from '../Dropdown/Dropdown'
import { SelectionHead } from './MultiSelect'
import './MultiSelect.css'

export interface PhotoOption { id: string; name: string; storedFileName?: string }

/** The grid shows at most this many matches; the rest are reachable through the search box,
    so opening the picker never fires hundreds of signed-URL requests at once. */
const MAX_GRID_RESULTS = 24

/** One lazy preview thumbnail - the signed URL is asked for only when the thumb actually renders. */
function PhotoThumb({ photo }: { photo: PhotoOption }) {
  const [url, setUrl] = useState<string | null>(null)
  useEffect(() => {
    if (photo.storedFileName === undefined) return
    let cancelled = false
    void fetchDownloadUrl(photo.storedFileName).then(link => { if (!cancelled) setUrl(link) }).catch(() => { })
    return () => { cancelled = true }
  }, [photo.storedFileName])
  if (url === null) return <span className="photo-thumb photo-thumb-empty" />
  return <img className="photo-thumb" src={url} alt="" loading="lazy" />
}

/** Photos as removable thumbnail chips plus a searchable thumbnail grid; clicking a grid cell toggles it without closing. */
export function PhotoMultiSelect({ photos, selected, onChange, addLabel, searchPlaceholder, ariaLabel }: {
  photos: PhotoOption[]
  selected: string[]
  onChange: (next: string[]) => void
  addLabel: string
  searchPlaceholder: string
  ariaLabel: string
}) {
  const [query, setQuery] = useState('')
  const filter = query.trim().toLowerCase()
  const selectedPhotos = selected
    .map(id => photos.find(photo => photo.id === id))
    .filter((photo): photo is PhotoOption => photo !== undefined)
  const matches = photos.filter(photo => !selected.includes(photo.id) && (filter === '' || photo.name.toLowerCase().includes(filter)))
  const toggle = (id: string) => onChange(selected.includes(id) ? selected.filter(value => value !== id) : [...selected, id])
  const hiddenCount = matches.length - MAX_GRID_RESULTS
  return <div className="multiselect">
    {selectedPhotos.length > 0 && <div className="multiselect-chips photo-chips">
      {selectedPhotos.map(photo => <span key={photo.id} className="multiselect-chip photo-chip" title={photo.name}>
        <PhotoThumb photo={photo} />
        <span className="photo-chip-name">{photo.name}</span>
        <button type="button" className="multiselect-chip-remove" aria-label={`Remove ${photo.name}`} onClick={() => toggle(photo.id)}>×</button>
      </span>)}
    </div>}
    <Dropdown align="start" panelClassName="multiselect-panel-root" trigger={({ toggle: toggleDropdown, isOpen }) =>
      <button type="button" className="field field-xs multiselect-trigger" aria-label={ariaLabel} aria-expanded={isOpen} onClick={toggleDropdown}>{addLabel}</button>}>
      {() => <div className="multiselect-picker">
        <input className="field field-xs multiselect-search" placeholder={searchPlaceholder} value={query} onChange={event => setQuery(event.target.value)} autoFocus />
        <SelectionHead count={selected.length} onClear={() => onChange([])} />
        <div className="photo-grid">
          {matches.slice(0, MAX_GRID_RESULTS).map(photo => <button type="button" key={photo.id} className="photo-grid-item" title={photo.name} onClick={() => toggle(photo.id)}>
            <PhotoThumb photo={photo} />
          </button>)}
        </div>
        {hiddenCount > 0 && <small className="multiselect-hint">{hiddenCount} more — refine the search</small>}
        {matches.length === 0 && <span className="multiselect-empty">No photos match</span>}
      </div>}
    </Dropdown>
  </div>
}
