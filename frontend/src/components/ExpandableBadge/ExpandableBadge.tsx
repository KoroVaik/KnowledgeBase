import type { ReactNode } from 'react'
import './ExpandableBadge.css'

export function ExpandableBadge({ label, color, textColor = '#111827', icon, className = '' }: {
  label: string
  color: string
  textColor?: string
  icon?: ReactNode
  className?: string
}) {
  return <span className={`expandable-badge ${className}`} style={{ backgroundColor: color, color: textColor }}
    tabIndex={0} role="img" aria-label={label}>
    <span className="expandable-badge-icon" aria-hidden="true">{icon}</span>
    <span className="expandable-badge-label" aria-hidden="true"><span>{label}</span></span>
  </span>
}
