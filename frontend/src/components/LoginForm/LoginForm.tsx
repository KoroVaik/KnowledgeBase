import { useLoginForm } from './useLoginForm'
import type { CurrentUser } from '../../api/auth'
import './LoginForm.css'

interface LoginFormProps {
  onSignedIn: (user: CurrentUser) => void
  googleSignInEnabled: boolean
}

export function LoginForm({ onSignedIn, googleSignInEnabled }: LoginFormProps) {
  const { password, state, inputRef, handlePasswordChange, handleSubmit, isSubmitting } =
    useLoginForm(onSignedIn)

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
        <button type="submit" className="btn btn-lg btn-primary" disabled={password === '' || isSubmitting}>
          {isSubmitting ? 'Signing in…' : 'Sign in'}
        </button>
      </form>

      {googleSignInEnabled && (
        <a className="login-google btn btn-lg" href="/api/auth/google/start">
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
