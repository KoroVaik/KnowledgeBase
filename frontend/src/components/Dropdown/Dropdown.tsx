import { useEffect, useRef, useState, type ReactNode, type TransitionEvent } from 'react'
import './Dropdown.css'

type DropdownTrigger = {
  isOpen: boolean
  open: () => void
  close: () => void
  toggle: () => void
}

type DropdownProps = {
  trigger: (props: DropdownTrigger) => ReactNode
  children: (close: () => void) => ReactNode
  onOpenChange?: (open: boolean) => void
  align?: 'start' | 'end'
  className?: string
  panelClassName?: string
}

export function Dropdown({
  trigger,
  children,
  onOpenChange,
  align = 'end',
  className,
  panelClassName,
}: DropdownProps) {
  const [isOpen, setIsOpen] = useState(false)
  const [isPresent, setIsPresent] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!isOpen) {
      return
    }

    function closeOnOutsideClick(event: MouseEvent) {
      if (containerRef.current !== null && !containerRef.current.contains(event.target as Node)) {
        setIsOpen(false)
        onOpenChange?.(false)
      }
    }

    function closeOnEscape(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setIsOpen(false)
        onOpenChange?.(false)
      }
    }

    document.addEventListener('mousedown', closeOnOutsideClick)
    document.addEventListener('keydown', closeOnEscape)
    return () => {
      document.removeEventListener('mousedown', closeOnOutsideClick)
      document.removeEventListener('keydown', closeOnEscape)
    }
  }, [isOpen, onOpenChange])

  function close() {
    setIsOpen(false)
    onOpenChange?.(false)
  }

  function open() {
    setIsPresent(true)
    onOpenChange?.(true)
    requestAnimationFrame(() => setIsOpen(true))
  }

  function toggle() {
    if (isOpen) {
      close()
      return
    }

    open()
  }

  function handleTransitionEnd(event: TransitionEvent<HTMLDivElement>) {
    if (!isOpen && event.target === event.currentTarget && event.propertyName === 'transform') {
      setIsPresent(false)
    }
  }

  const containerClassName = ['dropdown', className].filter(Boolean).join(' ')
  const panelClasses = ['dropdown-panel', `dropdown-panel-${align}`, panelClassName]
    .filter(Boolean)
    .join(' ')

  return (
    <div className={containerClassName} ref={containerRef}>
      {trigger({ isOpen, open, close, toggle })}
      {isPresent && (
        <div
          className={panelClasses}
          data-state={isOpen ? 'open' : 'closed'}
          onTransitionEnd={handleTransitionEnd}
        >
          {children(close)}
        </div>
      )}
    </div>
  )
}
