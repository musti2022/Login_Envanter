import { expect, test, type Page } from '@playwright/test'
import { api, signInAsMember, unique } from './support.ts'

interface Item {
  id: number
}

interface AssetDetails {
  id: number
  assetCode: string
  status: string
  rowVersion: string
  computerName: string | null
  activeAssignment: { userName: string; displayName: string } | null
  model: Item
  city: Item
  department: Item
  location: Item | null
}

interface AssignmentPage {
  items: { userName: string; returnedAt: string | null; returnedBy: string | null; assignmentDescription: string }[]
}

type Client = Awaited<ReturnType<typeof api>>

/** An available asset on lookups of its own. */
async function seedAsset(client: Client) {
  const prefix = unique('ZMT')
  const brand = await client.post<Item>('/api/brands', { name: `${prefix} Marka` })
  const model = await client.post<Item>('/api/models', { brandId: brand.id, name: 'M1' })
  const city = await client.post<Item>('/api/cities', { name: `${prefix} Şehir` })
  const department = await client.post<Item>('/api/departments', { name: `${prefix} Birim` })
  return client.post<AssetDetails>('/api/assets', {
    assetCode: `${prefix}-01`,
    assetType: 'Laptop',
    status: 'Available',
    modelId: model.id,
    cityId: city.id,
    departmentId: department.id,
    locationId: null,
  })
}

async function openAssignDialog(page: Page) {
  await page.getByRole('button', { name: 'Zimmet Ver' }).click()
  return page.getByRole('dialog', { name: 'Zimmet ver' })
}

test('an employee outside Bim_Envanter is given the asset from the detail page and returns it', async ({ page }) => {
  await signInAsMember(page)
  const client = await api(page)
  const asset = await seedAsset(client)
  await page.goto(`/envanter/${asset.id}`)
  await expect(page.getByText('Henüz zimmet kaydı yok')).toBeVisible()

  const dialog = await openAssignDialog(page)
  await dialog.getByRole('combobox', { name: 'Çalışan' }).fill('grup dışı')
  await page.getByRole('option', { name: /E2E Grup Dışı/ }).click()
  await dialog.getByRole('textbox', { name: 'Zimmet Tanımı' }).fill('Dizüstü bilgisayar + şarj adaptörü')
  await dialog.getByRole('textbox', { name: 'Not' }).fill('Kutusuyla teslim edildi')
  await dialog.getByRole('button', { name: 'Zimmet Ver' }).click()

  await expect(page.getByText(`${asset.assetCode}, E2E Grup Dışı adlı çalışana zimmetlendi.`)).toBeVisible()
  await expect(page.getByRole('dialog')).toBeHidden()
  await expect(page.getByText('Zimmetli Kişi').locator('xpath=following-sibling::dd[1]')).toHaveText('E2E Grup Dışı (e2e.outsider)')
  const periods = page.getByRole('list', { name: 'Zimmet geçmişi' })
  await expect(periods).toContainText('Zimmette')
  await expect(periods).toContainText('Kutusuyla teslim edildi')
  await expect(page.getByRole('list', { name: 'Demirbaş geçmişi' })).toContainText('Zimmetlendi')

  const assigned = await client.get<AssetDetails>(`/api/assets/${asset.id}`)
  expect(assigned.status).toBe('Assigned')
  expect(assigned.activeAssignment?.userName).toBe('e2e.outsider')

  await page.getByRole('button', { name: 'İade Al' }).click()
  const returnDialog = page.getByRole('dialog', { name: 'İade al' })
  await expect(returnDialog).toContainText('E2E Grup Dışı (e2e.outsider) adlı çalışandan iade alınacak')
  await returnDialog.getByRole('button', { name: 'İade Al' }).click()

  await expect(page.getByText(`${asset.assetCode} iade alındı.`)).toBeVisible()
  await expect(page.getByText('Bu demirbaş kimseye zimmetli değil.')).toBeVisible()
  await expect(periods).toContainText('İade alındı')
  await expect(periods).toContainText('E2E Grup Dışı (e2e.outsider)')
  await expect(page.getByRole('list', { name: 'Demirbaş geçmişi' })).toContainText('İade alındı')

  const returned = await client.get<AssetDetails>(`/api/assets/${asset.id}`)
  expect(returned.status).toBe('Available')
  const history = await client.get<AssignmentPage>(`/api/assets/${asset.id}/assignments`)
  expect(history.items).toHaveLength(1)
  expect(history.items[0]).toMatchObject({ userName: 'e2e.outsider', returnedBy: 'e2e.admin', assignmentDescription: 'Dizüstü bilgisayar + şarj adaptörü' })
})

test('a disabled account is not offered', async ({ page }) => {
  await signInAsMember(page)
  const asset = await seedAsset(await api(page))
  await page.goto(`/envanter/${asset.id}`)

  const dialog = await openAssignDialog(page)
  await dialog.getByRole('combobox', { name: 'Çalışan' }).fill('E2E Pasif')

  await expect(page.getByText('Bu adla etkin bir çalışan bulunamadı.')).toBeVisible()
  await expect(page.getByRole('option')).toHaveCount(0)
})

test('an assignment to an asset someone changed meanwhile is refused with a warning', async ({ page }) => {
  await signInAsMember(page)
  const client = await api(page)
  const asset = await seedAsset(client)
  await page.goto(`/envanter/${asset.id}`)
  const dialog = await openAssignDialog(page)
  await dialog.getByRole('combobox', { name: 'Çalışan' }).fill('grup')
  await page.getByRole('option', { name: /E2E Grup Dışı/ }).click()
  await dialog.getByRole('textbox', { name: 'Zimmet Tanımı' }).fill('Dizüstü')

  // Someone else edits the asset while the dialog is open.
  await client.put(`/api/assets/${asset.id}`, {
    assetCode: asset.assetCode,
    assetType: 'Laptop',
    status: 'Available',
    modelId: asset.model.id,
    cityId: asset.city.id,
    departmentId: asset.department.id,
    locationId: null,
    computerName: 'PC-BASKASI',
    rowVersion: asset.rowVersion,
  })
  await dialog.getByRole('button', { name: 'Zimmet Ver' }).click()

  await expect(dialog.getByText('Kayıt siz bakarken değiştirildi.')).toBeVisible()
  await expect(dialog.getByRole('button', { name: 'Zimmet Ver' })).toBeDisabled()
  await dialog.getByRole('button', { name: 'Vazgeç' }).click()
  await expect(page.getByText('Bilgisayar Adı').locator('xpath=following-sibling::dd[1]')).toHaveText('PC-BASKASI')
  expect((await client.get<AssetDetails>(`/api/assets/${asset.id}`)).status).toBe('Available')
})
