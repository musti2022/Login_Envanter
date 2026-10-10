import { readFile } from 'node:fs/promises'
import { expect, test } from '@playwright/test'
import { api, member, signInAsMember, unique } from './support.ts'
import { readSheet } from './xlsx.ts'

interface Item {
  id: number
}

test('the Excel file the browser saves holds the filtered list in the order on screen', async ({ page }, testInfo) => {
  await signInAsMember(page)
  const client = await api(page)
  const prefix = unique('EXCEL')
  const brand = await client.post<Item>('/api/brands', { name: `${prefix} Marka` })
  const model = await client.post<Item>('/api/models', { brandId: brand.id, name: 'M1' })
  const city = await client.post<Item>('/api/cities', { name: `${prefix} Şehir` })
  const department = await client.post<Item>('/api/departments', { name: `${prefix} Birim` })
  for (const [suffix, computerName, status] of [
    ['01', 'PC-B', 'Available'],
    ['02', 'PC-C', 'Faulty'],
    ['03', 'PC-A', 'Available'],
    ['04', 'PC-D', 'Retired'],
  ]) {
    await client.post('/api/assets', {
      assetCode: `${prefix}-${suffix}`,
      assetType: 'Laptop',
      status,
      modelId: model.id,
      cityId: city.id,
      departmentId: department.id,
      locationId: null,
      computerName,
      serialNumber: `${prefix}-SN-${suffix}`,
    })
  }

  await page.goto(`/envanter?search=${prefix}&status=Available&status=Faulty&sortBy=computerName&sortDirection=desc&pageSize=10`)
  await expect(page.getByRole('table', { name: 'Demirbaş listesi' }).getByRole('row')).toHaveCount(4)

  const saving = page.waitForEvent('download')
  await page.getByRole('button', { name: "Excel'e aktar" }).click()
  const download = await saving
  expect(download.suggestedFilename()).toMatch(/^envanter-\d{4}-\d{2}-\d{2}\.xlsx$/)
  const path = testInfo.outputPath(download.suggestedFilename())
  await download.saveAs(path)
  const file = await readFile(path)

  const list = readSheet(file, 'Envanter')
  expect(list[0]).toEqual([
    'Demirbaş Kodu',
    'Kullanıcı Adı',
    'Ad Soyad',
    'Bilgisayar Adı',
    'Marka',
    'Model',
    'Seri No',
    'Zimmet Tanımı',
    'Şehir',
    'Lokasyon',
    'Departman',
    'Tür',
    'Durum',
    'Eklenme Zamanı',
    'Son Değişiklik',
  ])
  // The retired asset is filtered out; the rest come in the screen's order (computer name, descending).
  expect(list.slice(1).map((row) => [row[0], row[3], row[4], row[11], row[12]])).toEqual([
    [`${prefix}-02`, 'PC-C', `${prefix} Marka`, 'Dizüstü', 'Arızalı'],
    [`${prefix}-01`, 'PC-B', `${prefix} Marka`, 'Dizüstü', 'Boşta'],
    [`${prefix}-03`, 'PC-A', `${prefix} Marka`, 'Dizüstü', 'Boşta'],
  ])

  const info = readSheet(file, 'Bilgi')
  expect(info).toContainEqual(['Oluşturan', member])
  expect(info).toContainEqual(['Demirbaş sayısı', '3'])
  expect(info).toContainEqual(['Arama', prefix])
  expect(info).toContainEqual(['Durum', 'Boşta, Arızalı'])
  expect(info).toContainEqual(['Sıralama', 'Bilgisayar Adı, azalan'])
})
