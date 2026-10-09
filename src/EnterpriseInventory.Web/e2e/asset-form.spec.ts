import { expect, test, type Page } from '@playwright/test'
import { api, signInAsMember, unique } from './support.ts'

interface Item {
  id: number
  name: string
}

interface Asset {
  id: number
  assetCode: string
  status: string
  assetType: string
  computerName: string | null
  serialNumber: string | null
  description: string | null
  brand: Item
  model: Item
  city: Item
  department: Item
  location: Item | null
  rowVersion: string
}

type Client = Awaited<ReturnType<typeof api>>

async function seedLookups(client: Client) {
  const prefix = unique('FORM')
  const brand = await client.post<Item>('/api/brands', { name: `${prefix} Marka` })
  const model = await client.post<Item>('/api/models', { brandId: brand.id, name: 'Model X' })
  const city = await client.post<Item>('/api/cities', { name: `${prefix} Şehir` })
  const location = await client.post<Item>('/api/locations', { cityId: city.id, name: 'Kat 3' })
  const department = await client.post<Item>('/api/departments', { name: `${prefix} Birim` })
  return { prefix, brand, model, city, location, department }
}

async function choose(page: Page, select: string, option: string) {
  await page.getByRole('combobox', { name: select }).click()
  await page.getByRole('option', { name: option, exact: true }).click()
}

function assetIdOf(page: Page) {
  return Number(new URL(page.url()).pathname.split('/').pop())
}

test('an asset is saved from the form', async ({ page }) => {
  await signInAsMember(page)
  const client = await api(page)
  const { prefix, brand, model, city, location, department } = await seedLookups(client)

  await page.goto('/envanter')
  await page.getByRole('link', { name: 'Yeni Demirbaş' }).click()
  await expect(page.getByRole('heading', { name: 'Yeni Demirbaş', level: 1 })).toBeVisible()
  await page.getByRole('textbox', { name: 'Demirbaş Kodu' }).fill(`${prefix}-01`)
  await choose(page, 'Tür', 'Dizüstü')
  await choose(page, 'Durum', 'Arızalı')
  await page.getByRole('textbox', { name: 'Bilgisayar Adı' }).fill('PC-FORM-01')
  await page.getByRole('textbox', { name: 'Seri No' }).fill(`${prefix}-SN`)
  await choose(page, 'Marka', brand.name)
  await choose(page, 'Model', model.name)
  await choose(page, 'Şehir', city.name)
  await choose(page, 'Lokasyon', location.name)
  await choose(page, 'Departman', department.name)
  await page.getByRole('textbox', { name: 'Açıklama' }).fill('Formdan eklendi; çantasıyla.')
  await page.getByRole('button', { name: 'Demirbaşı ekle' }).click()

  await expect(page).toHaveURL(/\/envanter\/\d+$/)
  const saved = await client.get<Asset>(`/api/assets/${assetIdOf(page)}`)
  expect(saved).toMatchObject({
    assetCode: `${prefix}-01`,
    assetType: 'Laptop',
    status: 'Faulty',
    computerName: 'PC-FORM-01',
    serialNumber: `${prefix}-SN`,
    description: 'Formdan eklendi; çantasıyla.',
    brand: { id: brand.id },
    model: { id: model.id },
    city: { id: city.id },
    location: { id: location.id },
    department: { id: department.id },
  })
})

test('the API refuses a code that is taken and the form says so under the field', async ({ page }) => {
  await signInAsMember(page)
  const client = await api(page)
  const { prefix, brand, model, city, department } = await seedLookups(client)
  await client.post('/api/assets', { assetCode: `${prefix}-02`, assetType: 'Monitor', modelId: model.id, cityId: city.id, departmentId: department.id })

  await page.goto('/envanter/yeni')
  await page.getByRole('textbox', { name: 'Demirbaş Kodu' }).fill(`${prefix.toLowerCase()}-02`)
  await choose(page, 'Tür', 'Monitör')
  await choose(page, 'Marka', brand.name)
  await choose(page, 'Model', model.name)
  await choose(page, 'Şehir', city.name)
  await choose(page, 'Departman', department.name)
  await page.getByRole('button', { name: 'Demirbaşı ekle' }).click()

  await expect(page.getByText('Bu demirbaş kodu başka bir kayıtta kullanılıyor.')).toBeVisible()
  await expect(page).toHaveURL(/\/envanter\/yeni$/)
})

test('an edit made on an outdated version is refused and redone on the current one', async ({ page }) => {
  await signInAsMember(page)
  const client = await api(page)
  const { prefix, model, city, department } = await seedLookups(client)
  const created = await client.post<Asset>('/api/assets', {
    assetCode: `${prefix}-03`,
    assetType: 'Desktop',
    modelId: model.id,
    cityId: city.id,
    departmentId: department.id,
    computerName: 'PC-ESKI',
  })

  await page.goto(`/envanter/${created.id}/duzenle`)
  await expect(page.getByRole('textbox', { name: 'Bilgisayar Adı' })).toHaveValue('PC-ESKI')

  // Another user saves first.
  await client.put(`/api/assets/${created.id}`, {
    assetCode: created.assetCode,
    assetType: created.assetType,
    status: created.status,
    modelId: model.id,
    cityId: city.id,
    departmentId: department.id,
    computerName: 'PC-DIGER',
    rowVersion: created.rowVersion,
  })

  await page.getByRole('textbox', { name: 'Seri No' }).fill(`${prefix}-SN-BENIM`)
  await page.getByRole('button', { name: 'Kaydet' }).click()
  const alert = page.getByRole('alert')
  await expect(alert).toContainText('başka bir kullanıcı tarafından değiştirildi')
  expect((await client.get<Asset>(`/api/assets/${created.id}`)).serialNumber).toBeNull()

  await alert.getByRole('button', { name: 'Güncel kaydı yükle' }).click()
  await expect(page.getByRole('textbox', { name: 'Bilgisayar Adı' })).toHaveValue('PC-DIGER')
  await page.getByRole('textbox', { name: 'Seri No' }).fill(`${prefix}-SN-BENIM`)
  await page.getByRole('button', { name: 'Kaydet' }).click()

  await expect(page).toHaveURL(new RegExp(`/envanter/${created.id}$`))
  const saved = await client.get<Asset>(`/api/assets/${created.id}`)
  expect(saved.computerName).toBe('PC-DIGER')
  expect(saved.serialNumber).toBe(`${prefix}-SN-BENIM`)
})

test('a missing brand and model are added without leaving the form', async ({ page }) => {
  await signInAsMember(page)
  const client = await api(page)
  const { prefix } = await seedLookups(client)

  await page.goto('/envanter/yeni')
  await page.getByRole('button', { name: 'Yeni marka ekle' }).click()
  await page.getByRole('dialog', { name: 'Yeni marka' }).getByRole('textbox', { name: 'Ad' }).fill(`${prefix} Yeni`)
  await page.getByRole('dialog', { name: 'Yeni marka' }).getByRole('button', { name: 'Ekle' }).click()
  await expect(page.getByRole('combobox', { name: 'Marka' })).toHaveText(`${prefix} Yeni`)

  await page.getByRole('button', { name: 'Yeni model ekle' }).click()
  await page.getByRole('dialog', { name: 'Yeni model' }).getByRole('textbox', { name: 'Ad' }).fill('Y1')
  await page.getByRole('dialog', { name: 'Yeni model' }).getByRole('button', { name: 'Ekle' }).click()
  await expect(page.getByRole('combobox', { name: 'Model' })).toHaveText('Y1')

  const brands = await client.get<Item[]>('/api/brands')
  const brand = brands.find((b) => b.name === `${prefix} Yeni`)
  expect(brand).toBeDefined()
  expect((await client.get<Item[]>(`/api/models?brandId=${brand!.id}`)).map((m) => m.name)).toEqual(['Y1'])
})
