import { expect, test, type Locator, type Page } from '@playwright/test'
import { api, signInAsMember, unique } from './support.ts'

interface Lookup {
  id: number
  name: string
  isActive: boolean
  rowVersion: string
}

interface Asset {
  id: number
  assetCode: string
  rowVersion: string
}

function section(page: Page, name: string) {
  return page.getByRole('region', { name: new RegExp(`^${name}`) })
}

function row(list: Locator, name: string) {
  return list.getByRole('row').filter({ has: list.page().getByRole('cell', { name, exact: true }) })
}

test('a brand and its model are added, renamed and deactivated, and a deactivated brand is not offered for new assets', async ({
  page,
}) => {
  await signInAsMember(page)
  const client = await api(page)
  const brand = `${unique('TNM')} Marka`
  const renamed = `${brand} Yeni`
  await page.goto('/tanimlar/marka-model')
  const brands = section(page, 'Markalar')
  const models = section(page, 'Modeller')

  await brands.getByRole('button', { name: 'Yeni marka' }).click()
  let dialog = page.getByRole('dialog', { name: 'Yeni marka' })
  await dialog.getByLabel('Ad').fill(brand)
  await dialog.getByRole('button', { name: 'Ekle' }).click()
  await expect(row(brands, brand)).toContainText('Aktif')

  await expect(models.getByRole('button', { name: 'Yeni model' })).toBeDisabled()
  await models.getByRole('combobox', { name: 'Marka' }).click()
  await page.getByRole('option', { name: brand, exact: true }).click()
  await models.getByRole('button', { name: 'Yeni model' }).click()
  dialog = page.getByRole('dialog', { name: 'Yeni model' })
  await expect(dialog).toContainText(`${brand} altına eklenecek.`)
  await dialog.getByLabel('Ad').fill('Model 1')
  await dialog.getByRole('button', { name: 'Ekle' }).click()
  await expect(row(models, 'Model 1')).toContainText(brand)

  await brands.getByRole('button', { name: `${brand} düzenle` }).click()
  dialog = page.getByRole('dialog', { name: 'Marka düzenle' })
  await dialog.getByLabel('Ad').fill(renamed)
  await dialog.getByRole('switch', { name: 'Aktif' }).click()
  await dialog.getByRole('button', { name: 'Kaydet' }).click()
  await expect(dialog).toBeHidden()
  await expect(row(brands, renamed)).toContainText('Pasif')
  await expect(row(models, 'Model 1')).toContainText(renamed)

  const saved = (await client.get<Lookup[]>('/api/brands')).find((b) => b.name === renamed)
  expect(saved?.isActive).toBe(false)
  const audit = await client.get<{ totalCount: number; items: { action: string; userName: string }[] }>(
    `/api/audit-logs?entityName=Brand&entityId=${saved!.id}`,
  )
  expect(audit.items.map((entry) => entry.action).sort()).toEqual(['Created', 'Updated'])

  // The new asset form no longer offers it.
  await page.goto('/envanter/yeni')
  await page.getByRole('combobox', { name: 'Marka' }).click()
  await expect(page.getByRole('listbox')).toBeVisible()
  await expect(page.getByRole('option', { name: renamed })).toHaveCount(0)
})

test('a change someone else saved meanwhile is refused with a warning and nothing is overwritten', async ({ page }) => {
  await signInAsMember(page)
  const client = await api(page)
  const city = await client.post<Lookup>('/api/cities', { name: `${unique('TNM')} Şehir` })
  await page.goto('/tanimlar/lokasyonlar')
  const cities = section(page, 'Şehirler')

  await cities.getByRole('button', { name: `${city.name} düzenle` }).click()
  const dialog = page.getByRole('dialog', { name: 'Şehir düzenle' })
  await dialog.getByLabel('Ad').fill(`${city.name} Benim`)

  // Someone else renames it while the dialog is open.
  await client.put(`/api/cities/${city.id}`, { name: `${city.name} Onların`, isActive: true, rowVersion: city.rowVersion })
  await dialog.getByRole('button', { name: 'Kaydet' }).click()

  await expect(dialog.getByRole('alert')).toContainText('Kayıt siz düzenlerken başka bir kullanıcı tarafından değiştirildi.')
  await expect(dialog.getByRole('button', { name: 'Kaydet' })).toBeDisabled()
  await dialog.getByRole('button', { name: 'Vazgeç' }).click()
  await expect(row(cities, `${city.name} Onların`)).toBeVisible()
  expect((await client.get<Lookup[]>('/api/cities')).find((c) => c.id === city.id)?.name).toBe(`${city.name} Onların`)
})

test('Zimmetler lists the assets assigned now and leads to the asset to take one back', async ({ page }) => {
  await signInAsMember(page)
  const client = await api(page)
  const prefix = unique('ZMTL')
  const brand = await client.post<Lookup>('/api/brands', { name: `${prefix} Marka` })
  const model = await client.post<Lookup>('/api/models', { brandId: brand.id, name: 'M1' })
  const city = await client.post<Lookup>('/api/cities', { name: `${prefix} Şehir` })
  const department = await client.post<Lookup>('/api/departments', { name: `${prefix} Birim` })
  const create = (code: string) =>
    client.post<Asset>('/api/assets', {
      assetCode: code,
      assetType: 'Laptop',
      status: 'Available',
      modelId: model.id,
      cityId: city.id,
      departmentId: department.id,
      locationId: null,
    })
  const assigned = await create(`${prefix}-01`)
  await create(`${prefix}-02`)
  const { items } = await client.get<{ items: { objectGuid: string }[] }>('/api/employees/search?q=e2e.outsider')
  await client.post(`/api/assets/${assigned.id}/assignments`, {
    employeeObjectGuid: items[0].objectGuid,
    assignmentDescription: 'Dizüstü bilgisayar',
    rowVersion: assigned.rowVersion,
  })

  await page.getByRole('navigation', { name: 'Ana menü' }).getByRole('link', { name: 'Zimmetler' }).click()
  await expect(page.getByRole('heading', { name: 'Zimmetler', level: 1 })).toBeVisible()
  await page.getByRole('searchbox', { name: 'Ara' }).fill(prefix)

  const table = page.getByRole('table', { name: 'Demirbaş listesi' })
  await expect(table).toContainText(`${prefix}-01`)
  await expect(table).toContainText('E2E Grup Dışı')
  await expect(table).not.toContainText(`${prefix}-02`)

  await table.getByRole('link', { name: `${prefix}-01`, exact: true }).click()
  await expect(page).toHaveURL(new RegExp(`/envanter/${assigned.id}$`))
  await expect(page.getByRole('button', { name: 'İade Al' })).toBeVisible()
})
