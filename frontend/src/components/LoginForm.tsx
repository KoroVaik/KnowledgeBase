import { useEffect, useRef, useState } from 'react'
import type { ChangeEvent, SubmitEvent } from 'react'
import { login } from '../api/auth'
import type { CurrentUser } from '../api/auth'

type LoginState =
  | { status: 'idle' }
  | { status: 'submitting' }
  | { status: 'error'; message: string }

interface LoginFormProps {
  onSignedIn: (user: CurrentUser) => void
}

export function LoginForm({ onSignedIn }: LoginFormProps) {
  const [password, setPassword] = useState('')
  const [state, setState] = useState<LoginState>({ status: 'idle' })

  const inputRef = useRef<HTMLInputElement>(null)

  // Focusing inside the catch block would be a no-op: the input is still disabled until
  // the failed state has rendered.
  useEffect(() => {
    if (state.status === 'error') {
      inputRef.current?.focus()
    }
  }, [state.status])

  function handlePasswordChange(event: ChangeEvent<HTMLInputElement>) {
    setPassword(event.target.value)

    if (state.status === 'error') {
      setState({ status: 'idle' })
    }
  }

  async function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault()
    setState({ status: 'submitting' })

    try {
      const user = await login(password)
      setPassword('')
      onSignedIn(user)
    } catch (error) {
      setPassword('')
      setState({
        status: 'error',
        message: error instanceof Error ? error.message : 'Unexpected error',
      })
    }
  }

  const isSubmitting = state.status === 'submitting'

  return (
    <section className="login">
      <form className="login-form" onSubmit={handleSubmit}>
        <label htmlFor="password">Password</label>
        <input
          id="password"
          ref={inputRef}
          type="password"
          value={password}
          onChange={handlePasswordChange}
          disabled={isSubmitting}
          autoComplete="current-password"
          autoFocus
        />
        <button type="submit" disabled={password === '' || isSubmitting}>
          {isSubmitting ? 'Signing in…' : 'Sign in'}
        </button>
      </form>

      {state.status === 'error' && (
        <p className="login-error" role="alert">
          {state.message}
        </p>
      )}
    </section>
  )
}
