import { expect, test, type Locator, type Page } from '@playwright/test'
import { api, signInAsMember, unique } from './support.ts'

interface Item {
  id: number
  name: string
}

interface AssetDetails {
  id: number
  assetCode: string
  rowVersion: string
  city: Item
  department: Item
  location: Item | null
}

/** Picks an option of a select in the dialog; a single select closes by itself. */
async function choose(page: Page, dialog: Locator, select: string, option: string) {
  await dialog.getByRole('combobox', { name: select }).click()
  await page.getByRole('option', { name: option, exact: true }).click()
}

test('an asset is moved to another city, location and department and the change is recorded', async ({ page }) => {
  await signInAsMember(page)
  const client = await api(page)
  const prefix = unique('KONUM')
  const brand = await client.post<Item>('/api/brands', { name: `${prefix} Marka` })
  const model = await client.post<Item>('/api/models', { brandId: brand.id, name: 'M1' })
  const izmir = await client.post<Item>('/api/cities', { name: `${prefix} İzmir` })
  const bursa = await client.post<Item>('/api/cities', { name: `${prefix} Bursa` })
  const office = await client.post<Item>('/api/locations', { cityId: izmir.id, name: 'Alsancak Ofis' })
  const depot = await client.post<Item>('/api/locations', { cityId: bursa.id, name: 'Nilüfer Depo' })
  const informatics = await client.post<Item>('/api/departments', { name: `${prefix} Bilgi İşlem` })
  const finance = await client.post<Item>('/api/departments', { name: `${prefix} Muhasebe` })
  const asset = await client.post<AssetDetails>('/api/assets', {
    assetCode: `${prefix}-01`,
    assetType: 'Desktop',
    modelId: model.id,
    cityId: izmir.id,
    departmentId: informatics.id,
    locationId: office.id,
  })

  await page.goto(`/envanter/${asset.id}`)
  await page.getByRole('button', { name: 'Konum Değiştir' }).click()
  const dialog = page.getByRole('dialog', { name: 'Konum değiştir' })
  await expect(dialog.getByRole('combobox', { name: 'Şehir' })).toHaveText(izmir.name)
  await expect(dialog.getByRole('combobox', { name: 'Lokasyon' })).toHaveText('Alsancak Ofis')
  await expect(dialog.getByRole('button', { name: 'Konumu Kaydet' })).toBeDisabled()

  await choose(page, dialog, 'Şehir', bursa.name)
  // Only the locations of the chosen city are offered.
  await dialog.getByRole('combobox', { name: 'Lokasyon' }).click()
  await expect(page.getByRole('option')).toHaveText(['Seçilmedi', 'Nilüfer Depo'])
  await page.getByRole('option', { name: 'Nilüfer Depo', exact: true }).click()
  await choose(page, dialog, 'Departman', finance.name)
  await dialog.getByRole('button', { name: 'Konumu Kaydet' }).click()

  await expect(page.getByText(`${asset.assetCode} konumu değiştirildi.`)).toBeVisible()
  await expect(page.getByRole('dialog')).toBeHidden()
  await expect(page.getByText('Lokasyon', { exact: true }).locator('xpath=following-sibling::dd[1]')).toHaveText('Nilüfer Depo')
  const entry = page.getByRole('list', { name: 'Demirbaş geçmişi' }).getByRole('listitem').first()
  await expect(entry).toContainText('Konumu değişti')
  await expect(entry).toContainText(`Şehir: ${izmir.name} → ${bursa.name}`)
  await expect(entry).toContainText('Lokasyon: Alsancak Ofis → Nilüfer Depo')

  const moved = await client.get<AssetDetails>(`/api/assets/${asset.id}`)
  expect([moved.city.id, moved.location?.id, moved.department.id]).toEqual([bursa.id, depot.id, finance.id])
})

test('a location of another city is refused by the API with a Turkish message', async ({ page }) => {
  await signInAsMember(page)
  const client = await api(page)
  const prefix = unique('KONUM')
  const brand = await client.post<Item>('/api/brands', { name: `${prefix} Marka` })
  const model = await client.post<Item>('/api/models', { brandId: brand.id, name: 'M1' })
  const city = await client.post<Item>('/api/cities', { name: `${prefix} Şehir` })
  const other = await client.post<Item>('/api/cities', { name: `${prefix} Diğer` })
  const elsewhere = await client.post<Item>('/api/locations', { cityId: other.id, name: 'Uzak Ofis' })
  const department = await client.post<Item>('/api/departments', { name: `${prefix} Birim` })
  const asset = await client.post<AssetDetails>('/api/assets', {
    assetCode: `${prefix}-01`,
    assetType: 'Laptop',
    modelId: model.id,
    cityId: city.id,
    departmentId: department.id,
  })

  // The screen never offers it; a caller that sends it anyway is refused under the field.
  const response = await page.request.put(`/api/assets/${asset.id}/location`, {
    data: { cityId: city.id, departmentId: department.id, locationId: elsewhere.id, rowVersion: asset.rowVersion },
    headers: { 'X-CSRF-TOKEN': ((await (await page.request.get('/api/auth/csrf')).json()) as { token: string }).token },
  })
  expect(response.status()).toBe(400)
  expect(((await response.json()) as { errors: Record<string, string[]> }).errors.locationId).toEqual(['Seçilen lokasyon seçilen şehirde değil.'])
  expect((await client.get<AssetDetails>(`/api/assets/${asset.id}`)).rowVersion).toBe(asset.rowVersion)
})
