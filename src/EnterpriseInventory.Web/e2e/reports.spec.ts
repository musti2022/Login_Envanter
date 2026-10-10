import { readFile } from 'node:fs/promises'
import { expect, test, type Page, type TestInfo } from '@playwright/test'
import { api, member, signInAsMember, unique } from './support.ts'
import { readSheet } from './xlsx.ts'

interface Item {
  id: number
}

interface Asset {
  id: number
  assetCode: string
  rowVersion: string
}

type Client = Awaited<ReturnType<typeof api>>

/** Four assets of one brand, three of model M1 (one faulty) and one of M2, on lookups of their own. */
async function seedAssets(client: Client, prefix: string) {
  const brand = await client.post<Item>('/api/brands', { name: `${prefix} Marka` })
  const m1 = await client.post<Item>('/api/models', { brandId: brand.id, name: 'M1' })
  const m2 = await client.post<Item>('/api/models', { brandId: brand.id, name: 'M2' })
  const city = await client.post<Item>('/api/cities', { name: `${prefix} Şehir` })
  const department = await client.post<Item>('/api/departments', { name: `${prefix} Birim` })
  const assets: Asset[] = []
  for (const [suffix, model, status] of [
    ['01', m1, 'Available'],
    ['02', m1, 'Available'],
    ['03', m1, 'Faulty'],
    ['04', m2, 'Available'],
  ] as const) {
    assets.push(
      await client.post<Asset>('/api/assets', {
        assetCode: `${prefix}-${suffix}`,
        assetType: 'Laptop',
        status,
        modelId: model.id,
        cityId: city.id,
        departmentId: department.id,
        locationId: null,
      }),
    )
  }
  return assets
}

async function download(page: Page, testInfo: TestInfo, name: RegExp) {
  const saving = page.waitForEvent('download')
  await page.getByRole('button', { name: "Excel'e aktar" }).click()
  const file = await saving
  expect(file.suggestedFilename()).toMatch(name)
  const path = testInfo.outputPath(file.suggestedFilename())
  await file.saveAs(path)
  return readFile(path)
}

const cellsOf = async (page: Page, table: string) =>
  page
    .getByRole('table', { name: table })
    .getByRole('row')
    .evaluateAll((rows) => rows.map((row) => [...row.querySelectorAll('th, td')].map((cell) => cell.textContent?.trim() ?? '')))

test('the inventory summary counts the filtered assets by model, saves the same table to Excel and opens each group in the inventory', async ({
  page,
}, testInfo) => {
  await signInAsMember(page)
  const client = await api(page)
  const prefix = unique('RAPOR')
  await seedAssets(client, prefix)

  await page.goto(`/raporlar?groupBy=model&search=${prefix}`)
  await expect(page.getByRole('tab', { name: 'Envanter özeti', selected: true })).toBeVisible()
  const table = 'Model bazında envanter özeti'
  await expect(page.getByRole('table', { name: table }).getByRole('row')).toHaveCount(4)
  expect(await cellsOf(page, table)).toEqual([
    ['Model', 'Toplam', 'Zimmetli', 'Boşta', 'Arızalı', 'Hurda'],
    [`${prefix} Marka M1`, '3', '0', '2', '1', '0'],
    [`${prefix} Marka M2`, '1', '0', '1', '0', '0'],
    ['Toplam', '4', '0', '3', '1', '0'],
  ])

  const file = await download(page, testInfo, /^envanter-ozeti-model-\d{4}-\d{2}-\d{2}\.xlsx$/)
  expect(readSheet(file, 'Özet')).toEqual([
    ['Model', 'Toplam', 'Zimmetli', 'Boşta', 'Arızalı', 'Hurda'],
    [`${prefix} Marka M1`, '3', '0', '2', '1', '0'],
    [`${prefix} Marka M2`, '1', '0', '1', '0', '0'],
    ['Toplam', '4', '0', '3', '1', '0'],
  ])
  const info = readSheet(file, 'Bilgi')
  expect(info).toContainEqual(['Rapor', 'Envanter özeti'])
  expect(info).toContainEqual(['Gruplama', 'Model'])
  expect(info).toContainEqual(['Oluşturan', member])
  expect(info).toContainEqual(['Demirbaş sayısı', '4'])
  expect(info).toContainEqual(['Arama', prefix])

  // The group's link opens the inventory with the report's filters and the group's own.
  await page.getByRole('link', { name: `${prefix} Marka M1` }).click()
  await expect(page).toHaveURL(/\/envanter\?/)
  await expect(page.getByRole('table', { name: 'Demirbaş listesi' }).getByRole('row')).toHaveCount(4)
  await expect(page.getByRole('table', { name: 'Demirbaş listesi' })).not.toContainText(`${prefix}-04`)
})

test('the movements report lists this month’s assignments and returns, newest first, and saves them to Excel', async ({
  page,
}, testInfo) => {
  await signInAsMember(page)
  const client = await api(page)
  const prefix = unique('HRKT')
  const [first, second] = await seedAssets(client, prefix)
  const { items } = await client.get<{ items: { objectGuid: string }[] }>('/api/employees/search?q=e2e.outsider')
  const employee = items[0].objectGuid

  // First is given and taken back; second is given and kept.
  const given = await client.post<Asset>(`/api/assets/${first.id}/assignments`, {
    employeeObjectGuid: employee,
    assignmentDescription: 'Dizüstü bilgisayar',
    rowVersion: first.rowVersion,
  })
  await client.post(`/api/assets/${first.id}/returns`, { rowVersion: given.rowVersion })
  await client.post(`/api/assets/${second.id}/assignments`, {
    employeeObjectGuid: employee,
    assignmentDescription: 'Dizüstü bilgisayar',
    rowVersion: second.rowVersion,
  })

  await page.goto('/raporlar')
  await page.getByRole('tab', { name: 'Zimmet hareketleri' }).click()
  await expect(page).toHaveURL(/\/raporlar\/zimmet-hareketleri/)
  await page.getByRole('button', { name: 'Bu ay' }).click()
  await page.getByRole('textbox', { name: 'Ara' }).fill(prefix)
  await page.getByRole('textbox', { name: 'Ara' }).press('Enter')

  const rows = page.getByRole('table', { name: 'Zimmet hareketleri' }).getByRole('row')
  await expect(rows).toHaveCount(4)
  await expect(page.getByText(/: 2 zimmet verildi, 1 iade alındı\.$/)).toBeVisible()
  const cells = await cellsOf(page, 'Zimmet hareketleri')
  // Newest first: the second asset's assignment, then the first one's return, then its assignment. The asset cell
  // holds the code, then the type.
  expect(cells.slice(1).map((row) => [row[1], row[2].slice(0, `${prefix}-01`.length)])).toEqual([
    ['Zimmet verildi', `${prefix}-02`],
    ['İade alındı', `${prefix}-01`],
    ['Zimmet verildi', `${prefix}-01`],
  ])
  expect(cells[1][4]).toContain('E2E Grup Dışı')
  expect(cells[1][6]).toContain(member)

  const file = await download(page, testInfo, /^zimmet-hareketleri-\d{4}-\d{2}-\d{2}\.xlsx$/)
  const sheet = readSheet(file, 'Hareketler')
  expect(sheet[0][0]).toBe('Tarih')
  expect(sheet.slice(1).map((row) => [row[1], row[2]])).toEqual([
    ['Zimmet verildi', `${prefix}-02`],
    ['İade alındı', `${prefix}-01`],
    ['Zimmet verildi', `${prefix}-01`],
  ])
  const info = readSheet(file, 'Bilgi')
  expect(info).toContainEqual(['Rapor', 'Zimmet hareketleri'])
  expect(info).toContainEqual(['Arama', prefix])
  expect(info).toContainEqual(['Zimmet verildi', '2'])
  expect(info).toContainEqual(['İade alındı', '1'])
})
