import { expect, test, type Page } from '@playwright/test'
import { api, signInAsMember, unique } from './support.ts'

interface Item {
  id: number
}

interface AssetDetails {
  id: number
  assetCode: string
}

/** An asset on lookups of its own, made through the API. */
async function seedAsset(page: Page) {
  const client = await api(page)
  const prefix = unique('DENETIM')
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
    computerName: 'PC-ESKI',
    serialNumber: `${prefix}-SN`,
  })
}

test('an edit made on the form can be followed in the audit log with its old and new values', async ({ page }) => {
  await signInAsMember(page)
  const asset = await seedAsset(page)

  await page.goto(`/envanter/${asset.id}/duzenle`)
  await page.getByRole('textbox', { name: 'Bilgisayar Adı' }).fill('PC-YENI')
  await page.getByRole('button', { name: 'Kaydet' }).click()
  await expect(page).toHaveURL(new RegExp(`/envanter/${asset.id}$`))

  // From the asset's history to its records in the audit log.
  await page.getByRole('link', { name: 'Denetim kayıtlarında aç' }).click()
  await expect(page).toHaveURL(new RegExp(`/denetim-gecmisi\\?entityName=Asset&entityId=${asset.id}$`))
  const table = page.getByRole('table', { name: 'Denetim kayıtları' })
  const rows = table.getByRole('row')
  await expect(rows).toHaveCount(3)
  const edit = rows.nth(1)
  await expect(edit).toContainText('e2e.admin')
  await expect(edit).toContainText('Güncellendi')
  await expect(edit).toContainText('Bilgisayar adı: PC-ESKI → PC-YENI')
  await expect(rows.nth(2)).toContainText('Eklendi')

  await edit.getByRole('button', { name: /ayrıntı/ }).click()
  const dialog = page.getByRole('dialog', { name: `Güncellendi: Demirbaş ${asset.assetCode}` })
  const values = dialog.getByRole('table', { name: 'Önceki ve yeni değerler' })
  await expect(values.getByRole('row', { name: /Bilgisayar adı/ })).toHaveText(/Bilgisayar adıPC-ESKIPC-YENI/)
  await dialog.getByRole('link', { name: asset.assetCode }).click()
  await expect(page).toHaveURL(new RegExp(`/envanter/${asset.id}$`))
})

test('the audit log filters by asset code and action on the server', async ({ page }) => {
  await signInAsMember(page)
  const asset = await seedAsset(page)

  await page.goto('/denetim-gecmisi')
  await page.getByRole('textbox', { name: 'Demirbaş kodu' }).fill(asset.assetCode.toLowerCase())
  await page.getByRole('combobox', { name: 'İşlem' }).click()
  await page.getByRole('option', { name: 'Eklendi' }).click()
  await page.keyboard.press('Escape')

  const rows = page.getByRole('table', { name: 'Denetim kayıtları' }).getByRole('row')
  await expect(rows).toHaveCount(2)
  await expect(rows.nth(1)).toContainText(asset.assetCode)
  await expect(rows.nth(1)).toContainText('Eklendi')
  await expect(page).toHaveURL(/assetCode=/)
})
