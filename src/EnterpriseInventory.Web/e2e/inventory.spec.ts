import { expect, test, type Page } from '@playwright/test'
import { api, signInAsMember, unique } from './support.ts'

interface Item {
  id: number
  name: string
}

interface AssetPage {
  items: { id: number; assetCode: string; rowVersion?: string }[]
  totalCount: number
}

interface AssetDetails {
  id: number
  assetCode: string
  rowVersion: string
}

type Client = Awaited<ReturnType<typeof api>>

/**
 * Lookups and twelve assets of its own, named with a prefix no other test uses, so a search for the prefix
 * returns exactly them however many assets the database holds.
 */
async function seed(client: Client) {
  const prefix = unique('E2E')
  const brandA = await client.post<Item>('/api/brands', { name: `${prefix} Marka A` })
  const brandB = await client.post<Item>('/api/brands', { name: `${prefix} Marka B` })
  const modelA1 = await client.post<Item>('/api/models', { brandId: brandA.id, name: 'A1' })
  const modelA2 = await client.post<Item>('/api/models', { brandId: brandA.id, name: 'A2' })
  const modelB1 = await client.post<Item>('/api/models', { brandId: brandB.id, name: 'B1' })
  const city = await client.post<Item>('/api/cities', { name: `${prefix} Şehir` })
  const office = await client.post<Item>('/api/locations', { cityId: city.id, name: 'Ofis' })
  const depot = await client.post<Item>('/api/locations', { cityId: city.id, name: 'Depo' })
  const department = await client.post<Item>('/api/departments', { name: `${prefix} Birim` })

  const models = [modelA1, modelA2, modelB1]
  const statuses = ['Available', 'Faulty', 'Retired']
  const types = ['Laptop', 'Desktop', 'Monitor', 'Printer']
  const assets: AssetDetails[] = []
  for (let i = 0; i < 12; i++) {
    assets.push(
      await client.post<AssetDetails>('/api/assets', {
        assetCode: `${prefix}-${String(i).padStart(2, '0')}`,
        assetType: types[i % types.length],
        status: statuses[i % statuses.length],
        modelId: models[i % models.length].id,
        cityId: city.id,
        departmentId: department.id,
        locationId: i % 2 === 0 ? office.id : depot.id,
        serialNumber: `${prefix}-SN-${String((i * 7) % 12).padStart(2, '0')}`,
      }),
    )
  }

  return { prefix, brandA, modelA2, assets }
}

/** The asset codes the table shows, top to bottom. */
function tableCodes(page: Page) {
  return page.getByRole('table', { name: 'Demirbaş listesi' }).locator('tbody tr td:first-child')
}

/** The table shows what the API returns for the query in the page address, in the same order. */
async function expectTableToMatchApi(page: Page, client: Client, expectedCount?: number) {
  await expect(async () => {
    const query = new URL(page.url()).search
    const result = await client.get<AssetPage>(`/api/assets${query}`)
    if (expectedCount !== undefined) expect(result.items).toHaveLength(expectedCount)
    await expect(tableCodes(page)).toHaveText(
      result.items.map((item) => item.assetCode),
      { timeout: 1000 },
    )
  }).toPass({ timeout: 15_000 })
}

async function choose(page: Page, select: string, option: string) {
  await page.getByRole('combobox', { name: select }).click()
  await page.getByRole('option', { name: option, exact: true }).click()
  // A multiple select stays open after a choice.
  if (await page.getByRole('listbox').isVisible()) await page.keyboard.press('Escape')
}

test('search, filters and sorting show what the API returns for the same query', async ({ page }) => {
  await signInAsMember(page)
  const client = await api(page)
  const { prefix, brandA, modelA2 } = await seed(client)
  await page.goto('/envanter')

  await page.getByRole('searchbox', { name: 'Ara' }).fill(prefix.toLowerCase())
  await expect(page).toHaveURL(new RegExp(`search=${prefix.toLowerCase()}`))
  await expectTableToMatchApi(page, client, 12)

  await choose(page, 'Marka', `${prefix} Marka A`)
  await expect(page).toHaveURL(new RegExp(`brandId=${brandA.id}`))
  await expectTableToMatchApi(page, client, 8)

  await choose(page, 'Model', 'A2')
  await expect(page).toHaveURL(new RegExp(`modelId=${modelA2.id}`))
  await expectTableToMatchApi(page, client, 4)

  await choose(page, 'Durum', 'Arızalı')
  await expect(page).toHaveURL(/status=Faulty/)
  await expectTableToMatchApi(page, client, 4)

  await choose(page, 'Model', 'Tümü')
  await choose(page, 'Tür', 'Dizüstü')
  await expect(page).toHaveURL(/assetType=Laptop/)
  await expectTableToMatchApi(page, client, 1)

  await page.getByRole('button', { name: 'Filtreleri temizle' }).click()
  await expect(page).toHaveURL(/\/envanter$/)
  await expect(page.getByRole('searchbox', { name: 'Ara' })).toHaveValue('')
})

test('sorting and paging walk through the result in the order of the API', async ({ page }) => {
  await signInAsMember(page)
  const client = await api(page)
  const { prefix } = await seed(client)
  await page.goto(`/envanter?search=${prefix}&pageSize=10`)
  await expectTableToMatchApi(page, client, 10)

  await page.getByRole('button', { name: 'Seri No' }).click()
  await page.getByRole('button', { name: 'Seri No' }).click()
  await expect(page).toHaveURL(/sortBy=serialNumber&sortDirection=desc/)
  await expectTableToMatchApi(page, client, 10)
  await expect(tableCodes(page).first()).toHaveText(`${prefix}-05`)

  await page.getByRole('button', { name: 'Sonraki sayfa' }).click()
  await expect(page).toHaveURL(/page=2/)
  await expectTableToMatchApi(page, client, 2)
  await expect(page.getByText('11–12 / 12')).toBeVisible()
})

test('archived assets are listed only on request and a search without matches says so', async ({ page }) => {
  await signInAsMember(page)
  const client = await api(page)
  const { prefix, assets } = await seed(client)
  const archived = assets[3]
  await client.delete(`/api/assets/${archived.id}?rowVersion=${encodeURIComponent(archived.rowVersion)}`)

  await page.goto(`/envanter?search=${prefix}`)
  await expectTableToMatchApi(page, client, 11)
  await page.getByRole('switch', { name: 'Arşivlenmişleri göster' }).check()
  await expect(page).toHaveURL(/archived=true/)
  await expectTableToMatchApi(page, client, 1)
  await expect(tableCodes(page)).toHaveText([archived.assetCode])

  await page.goto(`/envanter?search=${prefix}-YOK`)
  await expect(page.getByText('Filtrelerle eşleşen demirbaş yok')).toBeVisible()
})
