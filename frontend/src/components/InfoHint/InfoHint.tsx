import { useState } from 'react'
import './InfoHint.css'

/** An "i" icon with a tooltip shown on hover, keyboard focus or tap. Not the native `title`:
    it waits ~1 s and never shows on touch. The tap toggles explicit state because mobile
    Safari never focuses a tapped button, so hover/focus CSS alone would leave touch
    screens without the tooltip. */
export function InfoHint({ id, label, description }: { id: string; label: string; description: string }) {
  const [open, setOpen] = useState(false)
  const tooltipId = `${id}-tooltip`

  return (
    <span className="info-hint">
      <button
        type="button"
        className="info-hint-icon"
        aria-label={label}
        aria-describedby={tooltipId}
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
        onBlur={() => setOpen(false)}
      >
        i
      </button>
      <span id={tooltipId} role="tooltip" className={open ? 'info-hint-tooltip info-hint-tooltip-open' : 'info-hint-tooltip'}>
        {description}
      </span>
    </span>
  )
}
