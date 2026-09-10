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
  googleSignInEnabled: boolean
}

const googleErrors: Record<string, string> = {
  'google-not-allowed': 'That Google account is not allowed to sign in here.',
  'google-failed': 'Google sign-in did not complete.',
}

/** The Google flow has no response to catch: it reports back through the return URL. */
function readGoogleError(): LoginState {
  const code = new URLSearchParams(window.location.search).get('authError')

  return code === null || !(code in googleErrors)
    ? { status: 'idle' }
    : { status: 'error', message: googleErrors[code] }
}

export function LoginForm({ onSignedIn, googleSignInEnabled }: LoginFormProps) {
  const [password, setPassword] = useState('')
  const [state, setState] = useState<LoginState>(readGoogleError)

  // Drop the param once read into state, so a reload does not show a stale error.
  useEffect(() => {
    window.history.replaceState(null, '', window.location.pathname)
  }, [])

  const inputRef = useRef<HTMLInputElement>(null)

  // After render: the input is still disabled inside the catch block.
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

      {googleSignInEnabled && (
        <a className="login-google" href="/api/auth/google/start">
          Continue with Google
        </a>
      )}

      {state.status === 'error' && (
        <p className="login-error" role="alert">
          {state.message}
        </p>
      )}
    </section>
  )
}
