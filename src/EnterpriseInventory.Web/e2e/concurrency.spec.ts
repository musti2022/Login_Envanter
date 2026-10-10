import { expect, test, type Browser, type Locator, type Page } from '@playwright/test'
import { api, colleague, signInAsMember, unique } from './support.ts'

interface Item {
  id: number
}

interface AssetDetails {
  id: number
  assetCode: string
  rowVersion: string
  computerName: string | null
  serialNumber: string | null
  activeAssignment: { userName: string; displayName: string } | null
}

interface AssignmentPage {
  items: { userName: string; returnedAt: string | null }[]
}

/** An available asset on lookups of its own. */
async function seedAsset(page: Page) {
  const client = await api(page)
  const prefix = unique('YARIS')
  const brand = await client.post<Item>('/api/brands', { name: `${prefix} Marka` })
  const model = await client.post<Item>('/api/models', { brandId: brand.id, name: 'M1' })
  const city = await client.post<Item>('/api/cities', { name: `${prefix} Şehir` })
  const department = await client.post<Item>('/api/departments', { name: `${prefix} Birim` })
  const asset = await client.post<AssetDetails>('/api/assets', {
    assetCode: `${prefix}-01`,
    assetType: 'Laptop',
    status: 'Available',
    modelId: model.id,
    cityId: city.id,
    departmentId: department.id,
    locationId: null,
  })
  return { client, asset }
}

/** The second administrator, in a browser of their own as on another computer. */
async function colleagueBrowser(browser: Browser) {
  const context = await browser.newContext({ locale: 'tr-TR' })
  const page = await context.newPage()
  await signInAsMember(page, colleague)
  return { page, close: () => context.close() }
}

test('two administrators giving one asset away at the same moment leave exactly one holder', async ({ page, browser }) => {
  await signInAsMember(page)
  const { client, asset } = await seedAsset(page)
  const other = await colleagueBrowser(browser)

  // Both open the assignment on the same version of the asset and choose different people.
  const screens = [
    { page, search: 'grup dışı', name: 'E2E Grup Dışı', userName: 'e2e.outsider' },
    { page: other.page, search: 'E2E Yönetici', name: 'E2E Yönetici', userName: 'e2e.admin' },
  ]
  const dialogs: Locator[] = []
  for (const screen of screens) {
    await screen.page.goto(`/envanter/${asset.id}`)
    await screen.page.getByRole('button', { name: 'Zimmet Ver' }).click()
    const dialog = screen.page.getByRole('dialog', { name: 'Zimmet ver' })
    await dialog.getByRole('combobox', { name: 'Çalışan' }).fill(screen.search)
    await screen.page.getByRole('option', { name: new RegExp(screen.name) }).click()
    await dialog.getByRole('textbox', { name: 'Zimmet Tanımı' }).fill(`${screen.name} için dizüstü`)
    dialogs.push(dialog)
  }

  // Both press the button at the same moment.
  await Promise.all(dialogs.map((dialog) => dialog.getByRole('button', { name: 'Zimmet Ver' }).click()))

  // The API keeps one of them, whole: one holder, one open period, one audit record.
  await expect.poll(async () => (await client.get<AssetDetails>(`/api/assets/${asset.id}`)).activeAssignment).not.toBeNull()
  const saved = await client.get<AssetDetails>(`/api/assets/${asset.id}`)
  const winner = screens.findIndex((screen) => screen.userName === saved.activeAssignment!.userName)
  expect(winner).toBeGreaterThanOrEqual(0)
  const loser = 1 - winner
  const periods = await client.get<AssignmentPage>(`/api/assets/${asset.id}/assignments`)
  expect(periods.items).toEqual([expect.objectContaining({ userName: screens[winner].userName, returnedAt: null })])

  // The winner is told it worked; the other is told why it did not, and sees who holds the asset now.
  await expect(screens[winner].page.getByText(`${asset.assetCode}, ${screens[winner].name} adlı çalışana zimmetlendi.`)).toBeVisible()
  await expect(dialogs[loser].getByRole('alert')).toContainText(/Kayıt siz bakarken değiştirildi\.|Demirbaş zaten bir çalışana zimmetli\./)
  await expect(dialogs[loser].getByRole('button', { name: 'Zimmet Ver' })).toBeDisabled()
  await dialogs[loser].getByRole('button', { name: 'Vazgeç' }).click()
  await expect(screens[loser].page.getByText('Zimmetli Kişi').locator('xpath=following-sibling::dd[1]')).toHaveText(
    `${screens[winner].name} (${screens[winner].userName})`,
  )
  await other.close()
})

test('two administrators saving one asset at the same moment: one change is kept whole, the other is refused', async ({ page, browser }) => {
  await signInAsMember(page)
  const { client, asset } = await seedAsset(page)
  const other = await colleagueBrowser(browser)

  const screens = [
    { page, computerName: 'PC-BIRINCI', serialNumber: `${asset.assetCode}-SN-1` },
    { page: other.page, computerName: 'PC-IKINCI', serialNumber: `${asset.assetCode}-SN-2` },
  ]
  for (const screen of screens) {
    await screen.page.goto(`/envanter/${asset.id}/duzenle`)
    await screen.page.getByRole('textbox', { name: 'Bilgisayar Adı' }).fill(screen.computerName)
    await screen.page.getByRole('textbox', { name: 'Seri No' }).fill(screen.serialNumber)
  }

  await Promise.all(screens.map((screen) => screen.page.getByRole('button', { name: 'Kaydet' }).click()))

  // Never a mix of the two: both fields come from the same save.
  await expect.poll(async () => (await client.get<AssetDetails>(`/api/assets/${asset.id}`)).computerName).not.toBeNull()
  const saved = await client.get<AssetDetails>(`/api/assets/${asset.id}`)
  const winner = screens.findIndex((screen) => screen.computerName === saved.computerName)
  expect(winner).toBeGreaterThanOrEqual(0)
  expect(saved.serialNumber).toBe(screens[winner].serialNumber)
  const loser = 1 - winner

  await expect(screens[winner].page).toHaveURL(new RegExp(`/envanter/${asset.id}$`))
  const refusal = screens[loser].page.getByRole('form', { name: 'Demirbaş düzenleme formu' }).getByRole('alert')
  await expect(refusal).toContainText('başka bir kullanıcı tarafından değiştirildi')
  // The refused user's typing is still there, to redo on the current version.
  await expect(screens[loser].page.getByRole('textbox', { name: 'Bilgisayar Adı' })).toHaveValue(screens[loser].computerName)
  await other.close()
})
