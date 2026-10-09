import { expect, type APIRequestContext, type Page } from '@playwright/test'

// The fake users and their password come from playwright.config.ts.
export const password = process.env.EI_E2E_PASSWORD ?? ''
export const member = 'e2e.admin'

export async function signIn(page: Page, userName: string, secret: string) {
  await page.getByLabel('Kullanıcı adı').fill(userName)
  await page.getByLabel('Parola', { exact: true }).fill(secret)
  await page.getByRole('button', { name: 'Giriş Yap' }).click()
}

/** Signs in as the Bim_Envanter member and waits for the dashboard. */
export async function signInAsMember(page: Page) {
  await page.goto('/giris')
  await signIn(page, member, password)
  await expect(page.getByRole('heading', { name: 'Gösterge Paneli' })).toBeVisible()
}

/**
 * Calls the API with the page's session, the way the web app does: state-changing requests carry the CSRF
 * token. Used to prepare data and to read what the API says, to compare with the screen.
 */
export async function api(page: Page) {
  const request: APIRequestContext = page.request
  const { token } = (await (await request.get('/api/auth/csrf')).json()) as { token: string }
  const send = async <T>(method: string, path: string, data?: unknown): Promise<T> => {
    const response = await request.fetch(path, { method, data, headers: { 'X-CSRF-TOKEN': token } })
    expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true)
    return (response.status() === 204 ? undefined : await response.json()) as T
  }

  return {
    get: <T>(path: string) => send<T>('GET', path),
    post: <T>(path: string, data: unknown) => send<T>('POST', path, data),
    put: <T>(path: string, data: unknown) => send<T>('PUT', path, data),
    delete: (path: string) => send<void>('DELETE', path),
  }
}

/** A name no other test (or earlier run against the same database) uses. */
export function unique(prefix: string) {
  return `${prefix}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 7)}`.toUpperCase()
}
