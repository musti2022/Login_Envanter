import { ApiError, apiFetch, setCsrfToken } from '../api/http'

export interface CurrentUser {
  userName: string
  displayName: string
  roles: string[]
}

export interface Credentials {
  userName: string
  password: string
}

interface SignInResponse extends CurrentUser {
  csrfToken: string
}

/** The signed-in user, or null when there is no valid session. */
export async function fetchCurrentUser(signal?: AbortSignal): Promise<CurrentUser | null> {
  try {
    return await apiFetch<CurrentUser>('/api/auth/me', { signal, ignoreUnauthorized: true })
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return null
    }
    throw error
  }
}

/** Signs in; the password goes only into this request body and is not kept anywhere. */
export async function signIn(credentials: Credentials): Promise<CurrentUser> {
  const { csrfToken, ...user } = await apiFetch<SignInResponse>('/api/auth/login', {
    method: 'POST',
    body: credentials,
    ignoreUnauthorized: true,
  })
  // The token from before sign-in belongs to the anonymous visitor; this one belongs to the session.
  setCsrfToken(csrfToken)
  return user
}

export async function signOut(): Promise<void> {
  try {
    await apiFetch<void>('/api/auth/logout', { method: 'POST', ignoreUnauthorized: true })
  } catch (error) {
    // A session that already ended is as good as signed out.
    if (!(error instanceof ApiError && error.status === 401)) {
      throw error
    }
  } finally {
    setCsrfToken(null)
  }
}
