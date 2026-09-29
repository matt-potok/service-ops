import type { components } from './schema'

export type Session = components['schemas']['SessionResponse']
export type LoginRequest = components['schemas']['LoginRequest']

export async function getSession(): Promise<Session | null> {
  const response = await fetch('/api/v1/auth/me', { credentials: 'same-origin' })
  if (response.status === 401) return null
  if (!response.ok) throw new Error('Unable to load your session. Please try again.')
  return response.json()
}

export async function signIn(credentials: LoginRequest): Promise<void> {
  await post('/api/v1/auth/login', credentials)
}

export async function signOut(): Promise<void> {
  await post('/api/v1/auth/logout')
}

export async function getCsrfToken(): Promise<string> {
  // Fetch a fresh token because antiforgery tokens are bound to the current identity.
  const csrf = await fetch('/api/v1/auth/csrf', { credentials: 'same-origin' })
  if (!csrf.ok) throw new Error('Unable to connect. Please try again.')
  const { token } = await csrf.json() as components['schemas']['CsrfResponse']
  return token
}

async function post(path: string, body?: LoginRequest): Promise<void> {
  const token = await getCsrfToken()
  const response = await fetch(path, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token! },
    body: body ? JSON.stringify(body) : undefined,
  })
  if (!response.ok) {
    if (response.status === 429) throw new Error('Too many sign-in attempts. Please wait a minute and try again.')
    if (response.status === 401 && path.endsWith('/logout')) return
    const problem = await response.json().catch(() => null)
    throw new Error(problem?.detail ?? (response.status === 400
      ? 'Your session or form has changed. Please check the fields and try again.'
      : 'Unable to complete the request. Please try again.'))
  }
}
