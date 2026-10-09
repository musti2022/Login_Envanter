import { expect, test, type BrowserContext, type Page } from '@playwright/test'
import { member, password, signIn } from './support.ts'

const sessionCookie = '__Host-EnterpriseInventory'
const csrfCookie = '__Host-EnterpriseInventory.Csrf'

async function cookieNames(context: BrowserContext) {
  return (await context.cookies()).map((cookie) => cookie.name)
}

async function expectSignInPage(page: Page) {
  await expect(page).toHaveURL(/\/giris$/)
  await expect(page.getByRole('button', { name: 'Giriş Yap' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Gösterge Paneli' })).toHaveCount(0)
}

test.describe('a visitor without a session', () => {
  test('is sent to the Turkish sign-in page, wherever they start', async ({ page }) => {
    for (const path of ['/', '/envanter', '/denetim-gecmisi']) {
      await page.goto(path)
      await expectSignInPage(page)
    }

    await expect(page).toHaveTitle('Giriş | Kurumsal Envanter')
    await expect(page.getByRole('heading', { name: 'Kurumsal Envanter Yönetim Sistemi' })).toBeVisible()
    await expect(page.getByLabel('Kullanıcı adı')).toHaveAttribute('autocomplete', 'username')
    await expect(page.getByLabel('Parola', { exact: true })).toHaveAttribute('type', 'password')
  })

  test('gets nothing from the protected API', async ({ page }) => {
    await page.goto('/giris')

    for (const path of ['/api/auth/me', '/api/health', '/api/does-not-exist']) {
      const response = await page.request.get(path)
      expect(response.status(), path).toBe(401)
    }
  })
})

test.describe('signing in', () => {
  test('with a wrong password is refused with a Turkish message and gives no session', async ({ page, context }) => {
    await page.goto('/giris')

    await signIn(page, member, `${password}-yanlis`)

    await expect(page.getByRole('alert')).toContainText('Kullanıcı adı veya parola hatalı.')
    await expect(page.getByLabel('Parola', { exact: true })).toHaveValue('')
    await expect(page).toHaveURL(/\/giris$/)
    expect(await cookieNames(context)).not.toContain(sessionCookie)
  })

  test('as a user outside Bim_Envanter is refused even with the right password', async ({ page, context }) => {
    await page.goto('/giris')

    await signIn(page, 'e2e.outsider', password)

    await expect(page.getByRole('alert')).toContainText('Bu uygulamaya giriş yetkiniz yok.')
    expect(await cookieNames(context)).not.toContain(sessionCookie)
    await page.goto('/')
    await expectSignInPage(page)
    expect((await page.request.get('/api/auth/me')).status()).toBe(401)
  })

  test('with a disabled account is refused', async ({ page, context }) => {
    await page.goto('/giris')

    await signIn(page, 'e2e.disabled', password)

    await expect(page.getByRole('alert')).toContainText('Hesabınızla şu anda giriş yapılamıyor.')
    expect(await cookieNames(context)).not.toContain(sessionCookie)
  })

  test('as a member opens the page they asked for and then the dashboard', async ({ page }) => {
    await page.goto('/envanter')
    await expectSignInPage(page)

    await signIn(page, member, password)

    await expect(page).toHaveURL(/\/envanter$/)
    await expect(page.getByRole('heading', { name: 'Envanter', level: 1 })).toBeVisible()
    await expect(page.getByText('E2E Yönetici')).toBeVisible()

    await page.getByRole('link', { name: 'Gösterge Paneli' }).click()
    await expect(page).toHaveURL(/\/$/)
    await expect(page.getByRole('heading', { name: 'Gösterge Paneli' })).toBeVisible()

    const me = await page.request.get('/api/auth/me')
    expect(me.status()).toBe(200)
    expect(await me.json()).toEqual({ userName: member, displayName: 'E2E Yönetici', roles: ['Administrator'] })
  })

  test('keeps the session in HttpOnly cookies only, never in browser storage', async ({ page, context }) => {
    await page.goto('/giris')
    await signIn(page, member, password)
    await expect(page.getByRole('heading', { name: 'Gösterge Paneli' })).toBeVisible()

    const cookies = await context.cookies()
    for (const name of [sessionCookie, csrfCookie]) {
      const cookie = cookies.find((c) => c.name === name)
      expect(cookie, name).toMatchObject({ httpOnly: true, secure: true, sameSite: 'Strict', path: '/', expires: -1 })
    }

    const stored = await page.evaluate(() => ({
      local: localStorage.length,
      session: sessionStorage.length,
      cookies: document.cookie,
    }))
    expect(stored).toEqual({ local: 0, session: 0, cookies: '' })
    expect(await page.content()).not.toContain(password)

    await page.reload()
    await expect(page.getByRole('heading', { name: 'Gösterge Paneli' })).toBeVisible()
  })
})

test.describe('signing out', () => {
  test('ends the session on the server, so the old cookie no longer opens anything', async ({ page, context, browser }) => {
    await page.goto('/giris')
    await signIn(page, member, password)
    await expect(page.getByRole('heading', { name: 'Gösterge Paneli' })).toBeVisible()
    const signedInCookies = await context.cookies()

    await page.getByRole('button', { name: 'Çıkış Yap' }).click()

    await expectSignInPage(page)
    expect(await cookieNames(context)).not.toContain(sessionCookie)
    expect((await page.request.get('/api/auth/me')).status()).toBe(401)

    // Someone who copied the cookie before sign-out gets nothing with it.
    const replay = await browser.newContext({ baseURL: test.info().project.use.baseURL })
    try {
      await replay.addCookies(signedInCookies)
      const replayPage = await replay.newPage()
      await replayPage.goto('/')
      await expectSignInPage(replayPage)
      expect((await replayPage.request.get('/api/auth/me')).status()).toBe(401)
    } finally {
      await replay.close()
    }
  })
})
