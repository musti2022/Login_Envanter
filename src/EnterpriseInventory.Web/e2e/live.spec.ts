import { expect, test, type Browser, type Page, type WebSocketRoute } from '@playwright/test'
import { api, colleague, liveStatus, signInAsMember, unique } from './support.ts'

interface Item {
  id: number
  name: string
}

interface AssetDetails {
  id: number
  assetCode: string
  status: string
  rowVersion: string
}

/** A second administrator in a browser of their own, as on another computer. */
async function colleagueScreen(browser: Browser) {
  const context = await browser.newContext({ locale: 'tr-TR' })
  const page = await context.newPage()
  await signInAsMember(page, colleague)
  await expect(liveStatus(page)).toContainText('Canlı')
  return { page, close: () => context.close() }
}

/** Lookups of their own and one asset on them. */
async function assetOf(page: Page, prefix: string) {
  const client = await api(page)
  const brand = await client.post<Item>('/api/brands', { name: `${prefix} Marka` })
  const model = await client.post<Item>('/api/models', { brandId: brand.id, name: 'M1' })
  const city = await client.post<Item>('/api/cities', { name: `${prefix} Şehir` })
  const office = await client.post<Item>('/api/locations', { cityId: city.id, name: 'Merkez Ofis' })
  const depot = await client.post<Item>('/api/locations', { cityId: city.id, name: 'Arka Depo' })
  const department = await client.post<Item>('/api/departments', { name: `${prefix} Birim` })
  const asset = await client.post<AssetDetails>('/api/assets', {
    assetCode: `${prefix}-01`,
    assetType: 'Desktop',
    modelId: model.id,
    cityId: city.id,
    departmentId: department.id,
    locationId: office.id,
  })
  return { client, asset, brand, model, city, department, office, depot }
}

test('a move made on another screen shows up on the detail page without reloading', async ({ page, browser }) => {
  await signInAsMember(page)
  const { asset } = await assetOf(page, unique('CANLI'))
  await page.goto(`/envanter/${asset.id}`)
  await expect(liveStatus(page)).toContainText('Canlı')
  const location = page.getByText('Lokasyon', { exact: true }).locator('xpath=following-sibling::dd[1]')
  await expect(location).toHaveText('Merkez Ofis')
  const refreshes: string[] = []
  page.on('request', (request) => {
    if (request.url().endsWith(`/api/assets/${asset.id}`)) refreshes.push(request.headers()['x-background-request'] ?? 'user')
  })

  const other = await colleagueScreen(browser)
  await other.page.goto(`/envanter/${asset.id}`)
  await other.page.getByRole('button', { name: 'Konum Değiştir' }).click()
  const dialog = other.page.getByRole('dialog', { name: 'Konum değiştir' })
  await dialog.getByRole('combobox', { name: 'Lokasyon' }).click()
  await other.page.getByRole('option', { name: 'Arka Depo', exact: true }).click()
  await dialog.getByRole('button', { name: 'Konumu Kaydet' }).click()
  await expect(other.page.getByText(`${asset.assetCode} konumu değiştirildi.`)).toBeVisible()
  await other.close()

  // The first screen was not touched: the notification made it fetch the asset again, as a background read.
  await expect(location).toHaveText('Arka Depo')
  const entry = page.getByRole('list', { name: 'Demirbaş geçmişi' }).getByRole('listitem').first()
  await expect(entry).toContainText('Lokasyon: Merkez Ofis → Arka Depo')
  expect(refreshes).toContain('1')
})

test('an asset added on another screen appears in the open inventory list', async ({ page, browser }) => {
  await signInAsMember(page)
  const prefix = unique('CANLI')
  const { model, city, department } = await assetOf(page, prefix)
  await page.goto(`/envanter?search=${encodeURIComponent(prefix)}`)
  await expect(liveStatus(page)).toContainText('Canlı')
  const table = page.getByRole('table', { name: 'Demirbaş listesi' })
  await expect(table.getByRole('row')).toHaveCount(2)

  const other = await colleagueScreen(browser)
  await (await api(other.page)).post('/api/assets', {
    assetCode: `${prefix}-02`,
    assetType: 'Laptop',
    modelId: model.id,
    cityId: city.id,
    departmentId: department.id,
  })
  await other.close()

  await expect(table.getByRole('row', { name: new RegExp(`${prefix}-02`) })).toBeVisible()
  await expect(table.getByRole('row')).toHaveCount(3)
})

test('an edit is warned when another screen saves the asset first', async ({ page, browser }) => {
  await signInAsMember(page)
  const { asset } = await assetOf(page, unique('CANLI'))
  await page.goto(`/envanter/${asset.id}/duzenle`)
  await expect(liveStatus(page)).toContainText('Canlı')
  await page.getByRole('textbox', { name: 'Açıklama' }).fill('Benim değişikliğim')

  const other = await colleagueScreen(browser)
  const current = await (await api(other.page)).get<AssetDetails & Record<string, unknown>>(`/api/assets/${asset.id}`)
  await (await api(other.page)).put(`/api/assets/${asset.id}/location`, {
    cityId: (current.city as Item).id,
    departmentId: (current.department as Item).id,
    locationId: null,
    rowVersion: current.rowVersion,
  })
  await other.close()

  await expect(page.getByText(/siz düzenlerken başka bir kullanıcı tarafından değiştirildi/)).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Açıklama' })).toHaveValue('Benim değişikliğim')
})

test('changes made while the connection was down show up once it is back', async ({ page, browser }) => {
  // The live connection's socket goes through the test, which can cut it as a network outage would.
  const sockets: WebSocketRoute[] = []
  await page.routeWebSocket(/\/hubs\/inventory/, (socket) => {
    socket.connectToServer()
    sockets.push(socket)
  })
  await signInAsMember(page)
  const { asset, model, city, department, office } = await assetOf(page, unique('KOPMA'))
  await page.goto(`/envanter/${asset.id}`)
  await expect(liveStatus(page)).toContainText('Canlı')
  const computerName = page.getByText('Bilgisayar Adı', { exact: true }).locator('xpath=following-sibling::dd[1]')
  await expect(computerName).toHaveText('—')
  const refreshes: string[] = []
  page.on('request', (request) => {
    if (request.url().endsWith(`/api/assets/${asset.id}`)) refreshes.push(request.headers()['x-background-request'] ?? 'user')
  })

  // The network drops: the socket closes and the hub cannot be reached for a while.
  const outage = '**/hubs/inventory/negotiate**'
  await page.route(outage, (route) => route.abort('internetdisconnected'))
  await sockets.at(-1)!.close({ code: 4000, reason: 'Ağ kesintisi' })
  await expect(liveStatus(page)).toContainText('Yeniden bağlanıyor')

  // Meanwhile a colleague saves the asset; no notification can reach this screen.
  const other = await colleagueScreen(browser)
  await (await api(other.page)).put(`/api/assets/${asset.id}`, {
    assetCode: asset.assetCode,
    assetType: 'Desktop',
    status: asset.status,
    modelId: model.id,
    cityId: city.id,
    departmentId: department.id,
    locationId: office.id,
    computerName: 'PC-KESINTIDE',
    rowVersion: asset.rowVersion,
  })
  await other.close()
  await expect(computerName).toHaveText('—')
  expect(refreshes).toEqual([])

  // The network is back: the next try connects, and the screen is fetched again from the API.
  await page.unroute(outage)
  await expect(liveStatus(page)).toContainText('Canlı', { timeout: 20_000 })
  await expect(computerName).toHaveText('PC-KESINTIDE')
  expect(refreshes).toContain('1')
})

test('signing out in another tab ends the live connection here and asks to sign in again', async ({ page }) => {
  await signInAsMember(page)
  await page.goto('/envanter')
  await expect(liveStatus(page)).toContainText('Canlı')

  const otherTab = await page.context().newPage()
  await otherTab.goto('/')
  await otherTab.getByRole('button', { name: 'Çıkış Yap' }).click()
  await expect(otherTab.getByRole('button', { name: 'Giriş Yap' })).toBeVisible()

  // The API closed this tab's connection; trying again finds the session ended.
  await expect(page).toHaveURL(/\/giris$/)
  await expect(page.getByText('Oturumunuz sona erdi. Devam etmek için tekrar giriş yapın.')).toBeVisible()
})
