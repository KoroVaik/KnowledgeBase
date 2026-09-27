import { Children, Component, Fragment, cloneElement, isValidElement, useCallback, useLayoutEffect, useRef } from 'react'
import type { ElementType, ReactElement, ReactNode, Ref } from 'react'
import { reconcileRows } from './transitionRows'
import type { TransitionRow } from './transitionRows'

const DURATION = 380
const reducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches

type RowElement = ReactElement<{ ref?: Ref<HTMLElement>; inert?: boolean }>
interface RowsProps { children: ReactNode; resetKey: string }
interface RowsState { source: ReactNode; resetKey: string; rows: TransitionRow<RowElement>[] }

function keyedRows(children: ReactNode) {
  return Children.toArray(children).filter(isValidElement).map(node => ({ key: String(node.key), value: node as RowElement }))
}

function AnimatedRow({ row, delay, onExit, onEntered, register }: {
  row: TransitionRow<RowElement>
  delay: number
  onExit: (key: string) => void
  onEntered: (key: string) => void
  register: (key: string, node: HTMLElement | null) => void
}) {
  const nodeRef = useRef<HTMLElement | null>(null)
  const attachRef = useCallback((node: HTMLElement | null) => {
    nodeRef.current = node
    if (node) node.style.overflowAnchor = 'none'
    register(row.key, node)
  }, [register, row.key])

  useLayoutEffect(() => {
    const node = nodeRef.current
    if (!node) return
    if (!row.exiting) {
      delete node.dataset.listExiting
      if (!row.entering) return
      const animation = node.animate([{ opacity: 0 }, { opacity: 1 }], {
        duration: reducedMotion() ? 120 : 200, delay, fill: 'backwards',
      })
      animation.onfinish = () => onEntered(row.key)
      return () => animation.cancel()
    }

    node.dataset.listExiting = 'true'
    const style = getComputedStyle(node)
    const animation = node.animate(reducedMotion()
      ? [{ opacity: 1 }, { opacity: 0 }]
      : [{
        height: `${node.getBoundingClientRect().height}px`, minHeight: 0,
        paddingTop: style.paddingTop, paddingBottom: style.paddingBottom,
        marginTop: style.marginTop, marginBottom: style.marginBottom,
        borderTopWidth: style.borderTopWidth, borderBottomWidth: style.borderBottomWidth, opacity: 1,
      }, {
        height: 0, minHeight: 0, paddingTop: 0, paddingBottom: 0,
        marginTop: 0, marginBottom: 0, borderTopWidth: 0, borderBottomWidth: 0, opacity: 0,
      }], { duration: reducedMotion() ? 120 : DURATION, easing: 'ease-in-out', fill: 'forwards' })
    animation.onfinish = () => onExit(row.key)
    return () => animation.cancel()
  }, [row.exiting, row.entering, row.key, delay, onExit, onEntered])

  const Row = row.value.type as ElementType<RowElement['props']>
  return <Row {...row.value.props} inert={row.exiting || row.value.props.inert} ref={attachRef} />
}

// React's snapshot runs before DOM updates, so movement starts at the actual old position.
class AnimatedRows extends Component<RowsProps, RowsState, Map<string, DOMRect>> {
  state: RowsState = {
    source: this.props.children, resetKey: this.props.resetKey,
    rows: keyedRows(this.props.children).map(row => ({ ...row, exiting: false, entering: false })),
  }
  private nodes = new Map<string, HTMLElement>()
  private movements = new Map<string, Animation>()

  static getDerivedStateFromProps(props: RowsProps, state: RowsState): RowsState | null {
    if (props.children === state.source && props.resetKey === state.resetKey) return null
    const incoming = keyedRows(props.children)
    return {
      source: props.children, resetKey: props.resetKey,
      rows: props.resetKey === state.resetKey
        ? reconcileRows(state.rows, incoming)
        : incoming.map(row => ({ ...row, exiting: false, entering: false })),
    }
  }

  getSnapshotBeforeUpdate() {
    return new Map([...this.nodes].map(([key, node]) => [key, node.getBoundingClientRect()]))
  }

  componentDidUpdate(_props: RowsProps, previous: RowsState, positions: Map<string, DOMRect>) {
    if (previous.resetKey !== this.state.resetKey || reducedMotion()) {
      this.movements.forEach(animation => animation.cancel())
      this.movements.clear()
      return
    }
    for (const row of this.state.rows) {
      const node = this.nodes.get(row.key)
      const before = positions.get(row.key)
      if (!node || !before) continue
      this.movements.get(row.key)?.cancel()
      const after = node.getBoundingClientRect()
      const x = before.left - after.left
      const y = before.top - after.top
      if (Math.abs(x) < 1 && Math.abs(y) < 1) continue
      const animation = node.animate([
        { transform: `translate(${x}px, ${y}px)` }, { transform: 'translate(0, 0)' },
      ], { duration: DURATION, easing: 'ease-in-out' })
      this.movements.set(row.key, animation)
      animation.onfinish = () => { if (this.movements.get(row.key) === animation) this.movements.delete(row.key) }
    }
  }

  componentWillUnmount() {
    this.movements.forEach(animation => animation.cancel())
  }

  private register = (key: string, node: HTMLElement | null) => {
    if (node) this.nodes.set(key, node)
    else {
      this.nodes.delete(key)
      this.movements.get(key)?.cancel()
      this.movements.delete(key)
    }
  }

  private remove = (key: string) => {
    this.setState(state => {
      const remaining = state.rows.filter(row => row.key !== key || !row.exiting)
      return { rows: remaining.some(row => row.exiting) ? remaining : reconcileRows(remaining, keyedRows(state.source)) }
    })
  }

  private entered = (key: string) => {
    this.setState(state => ({ rows: state.rows.map(row => row.key === key ? { ...row, entering: false } : row) }))
  }

  render() {
    const delay = this.state.rows.some(row => row.exiting) && !reducedMotion() ? DURATION : 0
    return this.state.rows.map(row => <AnimatedRow key={row.key} row={row} delay={delay} onExit={this.remove} onEntered={this.entered} register={this.register} />)
  }
}

export function AnimatedList({ children, resetKey = '' }: { children: ReactNode; resetKey?: string }) {
  if (isValidElement<{ children?: ReactNode }>(children) &&
    (children.type === 'ul' || children.type === 'div' || children.type === Fragment)) {
    return cloneElement(children, {}, <AnimatedRows resetKey={resetKey}>{children.props.children}</AnimatedRows>)
  }
  return <AnimatedRows resetKey={resetKey}>{children}</AnimatedRows>
}
