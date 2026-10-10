import { expect, test, type Page, type TestInfo } from '@playwright/test'
import { api, signInAsMember, unique } from './support.ts'

interface Item {
  id: number
  name: string
}

// A phone held upright.
test.use({ viewport: { width: 390, height: 844 } })

/** The page itself never scrolls sideways; a wide table scrolls inside its own box. */
async function expectToFitTheWidth(page: Page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
  expect(overflow, 'horizontal overflow in pixels').toBeLessThanOrEqual(0)
}

async function screenshot(page: Page, testInfo: TestInfo, name: string) {
  await testInfo.attach(name, { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' })
}

test('on a phone every screen fits the width and the main tasks work', async ({ page }, testInfo) => {
  await signInAsMember(page)
  const client = await api(page)
  const prefix = unique('MOBIL')
  const brand = await client.post<Item>('/api/brands', { name: `${prefix} Marka` })
  const model = await client.post<Item>('/api/models', { brandId: brand.id, name: 'Model M' })
  const city = await client.post<Item>('/api/cities', { name: `${prefix} Şehir` })
  const department = await client.post<Item>('/api/departments', { name: `${prefix} Birim` })
  const asset = await client.post<Item & { assetCode: string }>('/api/assets', {
    assetCode: `${prefix}-01`,
    assetType: 'Tablet',
    status: 'Faulty',
    modelId: model.id,
    cityId: city.id,
    departmentId: department.id,
    description: 'Uzun bir açıklama: ekranı çatlak, şarj aleti eksik, kılıfı yıpranmış; onarım için servise gönderilecek.',
  })

  await expect(page.getByRole('heading', { name: 'Gösterge Paneli' })).toBeVisible()
  await expectToFitTheWidth(page)
  await screenshot(page, testInfo, 'gosterge-paneli')

  await page.getByRole('button', { name: 'Menüyü aç' }).click()
  await page.getByRole('navigation', { name: 'Ana menü' }).getByRole('link', { name: 'Envanter' }).click()
  await expect(page.getByRole('heading', { name: 'Envanter', level: 1 })).toBeVisible()
  await page.getByRole('searchbox', { name: 'Ara' }).fill(prefix)
  await expect(page.getByRole('table', { name: 'Demirbaş listesi' }).getByRole('link', { name: asset.assetCode, exact: true })).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Durum' })).toBeHidden()
  await page.getByRole('button', { name: 'Filtreler', exact: true }).click()
  await page.getByRole('combobox', { name: 'Durum' }).click()
  await page.getByRole('option', { name: 'Arızalı', exact: true }).click()
  await page.keyboard.press('Escape')
  await expect(page).toHaveURL(/status=Faulty/)
  await expect(page.getByRole('button', { name: 'Filtreler, 1 filtre açık' })).toBeVisible()
  await expectToFitTheWidth(page)
  // The page buttons are not cut off at the card's edge. (Measured as boxes: the pagination box clips its content,
  // and a fraction of a pixel lost to rounding there made a "fully in view" check fail now and then.)
  const lastPage = page.getByRole('button', { name: 'Son sayfa' })
  await lastPage.scrollIntoViewIfNeeded()
  await expect(lastPage).toBeInViewport({ ratio: 0.95 })
  const button = (await lastPage.boundingBox())!
  const card = (await page.locator('.MuiCard-root', { has: lastPage }).boundingBox())!
  expect(button.x).toBeGreaterThanOrEqual(card.x)
  expect(button.x + button.width).toBeLessThanOrEqual(card.x + card.width + 0.5)
  expect(button.y + button.height).toBeLessThanOrEqual(card.y + card.height + 0.5)
  await screenshot(page, testInfo, 'envanter')

  await page.getByRole('link', { name: `${asset.assetCode} detayı` }).click()
  await expect(page.getByRole('heading', { name: asset.assetCode, level: 1 })).toBeVisible()
  await expect(page.getByRole('list', { name: 'Demirbaş geçmişi' })).toBeVisible()
  await expectToFitTheWidth(page)
  await screenshot(page, testInfo, 'detay')

  await page.getByRole('link', { name: 'Düzenle' }).click()
  await expect(page.getByRole('textbox', { name: 'Demirbaş Kodu' })).toHaveValue(asset.assetCode)
  await expectToFitTheWidth(page)
  await screenshot(page, testInfo, 'duzenleme')
  await page.getByRole('button', { name: 'Vazgeç' }).click()

  await page.getByRole('button', { name: 'Arşivle' }).click()
  await page.getByRole('dialog', { name: 'Demirbaşı arşivle' }).getByRole('button', { name: 'Arşivle' }).click()
  await expect(page.getByText(`${asset.assetCode} arşivlendi.`)).toBeVisible()
  await expect(page.getByText('Bu demirbaş arşivlenmiş; yalnızca görüntülenebilir.')).toBeVisible()
  await expectToFitTheWidth(page)

  await page.getByRole('link', { name: 'Denetim kayıtlarında aç' }).click()
  const auditRows = page.getByRole('table', { name: 'Denetim kayıtları' }).getByRole('row')
  await expect(auditRows.nth(1)).toContainText('Arşivlendi')
  await expectToFitTheWidth(page)
  await screenshot(page, testInfo, 'denetim-gecmisi')
  await auditRows.nth(1).getByRole('button', { name: /ayrıntı/ }).click()
  await expect(page.getByRole('dialog', { name: `Arşivlendi: Demirbaş ${asset.assetCode}` })).toBeVisible()
  await expectToFitTheWidth(page)
  await screenshot(page, testInfo, 'denetim-kaydi')
  await page.getByRole('button', { name: 'Kapat' }).click()

  await page.goto('/envanter/yeni')
  await expect(page.getByRole('button', { name: 'Demirbaşı ekle' })).toBeVisible()
  await expectToFitTheWidth(page)
  await screenshot(page, testInfo, 'yeni')
})

test('a missing asset and a page that does not exist say so on a phone too', async ({ page }) => {
  await signInAsMember(page)

  await page.goto('/envanter/999999999')
  await expect(page.getByText('Demirbaş bulunamadı')).toBeVisible()
  await expectToFitTheWidth(page)

  await page.goto('/envanter/999999999/duzenle')
  await expect(page.getByText('Demirbaş bulunamadı')).toBeVisible()

  await page.goto('/olmayan-sayfa')
  await expect(page.getByText('Aradığınız sayfa bulunamadı')).toBeVisible()
  await expectToFitTheWidth(page)
})

test('the assignments and definitions screens fit a phone', async ({ page }, testInfo) => {
  await signInAsMember(page)

  for (const [path, heading, name] of [
    ['/zimmetler', 'Zimmetler', 'zimmetler'],
    ['/tanimlar/marka-model', 'Marka ve Modeller', 'marka-model'],
    ['/tanimlar/lokasyonlar', 'Lokasyonlar', 'lokasyonlar'],
  ]) {
    await page.goto(path)
    await expect(page.getByRole('heading', { name: heading, level: 1 })).toBeVisible()
    await expect(page.getByRole('progressbar')).toHaveCount(0)
    await expectToFitTheWidth(page)
    await screenshot(page, testInfo, name)
  }

  await page.getByRole('region', { name: /^Şehirler/ }).getByRole('button', { name: /düzenle$/ }).first().click()
  await expect(page.getByRole('dialog', { name: 'Şehir düzenle' })).toBeVisible()
  await expectToFitTheWidth(page)
})
