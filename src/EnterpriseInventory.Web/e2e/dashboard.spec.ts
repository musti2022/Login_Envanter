import { expect, test } from '@playwright/test'
import { api, signInAsMember } from './support.ts'

interface Statistics {
  totalCount: number
  assignedCount: number
  availableCount: number
  faultyCount: number
  byCity: {
    items: { name: string; count: number; assignedCount: number }[]
    otherGroupCount: number
    otherCount: number
    otherAssignedCount: number
  }
  byType: { assetType: string; count: number }[]
  monthlyMovements: { month: string; assignedCount: number; returnedCount: number }[]
}

const typeLabels: Record<string, string> = {
  Desktop: 'Masaüstü',
  Laptop: 'Dizüstü',
  Monitor: 'Monitör',
  Printer: 'Yazıcı',
  Phone: 'Telefon',
  Tablet: 'Tablet',
  Server: 'Sunucu',
  NetworkDevice: 'Ağ cihazı',
  Peripheral: 'Çevre birimi',
  Other: 'Diğer',
}

const numberFormat = new Intl.NumberFormat('tr-TR')

test('the dashboard shows the figures the database holds', async ({ page }, testInfo) => {
  await signInAsMember(page)
  const client = await api(page)

  // Other tests add assets at the same time, so the screen and the API are compared until they agree.
  await expect(async () => {
    await page.reload()
    await expect(page.getByRole('link', { name: /Toplam Demirbaş/ })).toBeVisible()
    const statistics = await client.get<Statistics>('/api/dashboard/statistics')

    for (const [label, value] of [
      ['Toplam Demirbaş', statistics.totalCount],
      ['Zimmetli', statistics.assignedCount],
      ['Boşta', statistics.availableCount],
      ['Arızalı', statistics.faultyCount],
    ] as const) {
      await expect(page.getByRole('link', { name: new RegExp(`^${label}`) })).toHaveText(`${label}${numberFormat.format(value)}`, {
        timeout: 1000,
      })
    }

    const cities = page.getByRole('list', { name: 'Şehirlere göre demirbaş sayısı' })
    const { items, otherGroupCount, otherCount, otherAssignedCount } = statistics.byCity
    if (items.length > 0) {
      const rows = items.map((city) => [city.name, city.assignedCount, city.count] as const)
      if (otherGroupCount > 0) {
        rows.push([`Diğer ${numberFormat.format(otherGroupCount)} şehir`, otherAssignedCount, otherCount])
      }
      await expect(cities.getByRole('listitem')).toHaveText(
        rows.map(([name, assigned, count]) => `${name}${numberFormat.format(assigned)} zimmetli · ${numberFormat.format(count)}`),
        { timeout: 1000 },
      )
      // Eight cities at most, the rest together: the list stays short however many cities there are.
      expect(items.length).toBeLessThanOrEqual(8)
    } else {
      await expect(cities).toHaveCount(0)
    }

    if (statistics.byType.length > 0) {
      await expect(page.getByRole('list', { name: 'Türlere göre demirbaş sayısı' }).getByRole('listitem')).toHaveText(
        statistics.byType.map((type) => `${typeLabels[type.assetType]}${numberFormat.format(type.count)}`),
        { timeout: 1000 },
      )
    }

    // The current month, as the bar's label tells it.
    const thisMonth = statistics.monthlyMovements.at(-1)!
    await expect(page.getByRole('list', { name: 'Aylık zimmet hareketleri' }).getByRole('listitem').last()).toHaveAccessibleName(
      new RegExp(
        `: ${numberFormat.format(thisMonth.assignedCount)} zimmet, ${numberFormat.format(thisMonth.returnedCount)} iade$`,
      ),
      { timeout: 1000 },
    )
  }).toPass({ timeout: 20_000 })

  await page.screenshot({ path: testInfo.outputPath('dashboard.png'), fullPage: true })
})

test('a figure opens the inventory filtered to what it counts', async ({ page }) => {
  await signInAsMember(page)

  await page.getByRole('link', { name: /^Zimmetli/ }).click()

  await expect(page).toHaveURL(/\/envanter\?status=Assigned$/)
  await expect(page.getByRole('heading', { name: 'Envanter', level: 1 })).toBeVisible()
})
