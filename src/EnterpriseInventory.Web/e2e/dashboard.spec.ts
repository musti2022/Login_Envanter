import { expect, test } from '@playwright/test'
import { api, signInAsMember } from './support.ts'

interface Statistics {
  totalCount: number
  assignedCount: number
  availableCount: number
  faultyCount: number
  byCity: { name: string; count: number }[]
}

const numberFormat = new Intl.NumberFormat('tr-TR')

test('the dashboard shows the figures the database holds', async ({ page }) => {
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
    if (statistics.byCity.length > 0) {
      await expect(cities.getByRole('listitem')).toHaveText(
        statistics.byCity.map((city) => `${city.name}${numberFormat.format(city.count)}`),
        { timeout: 1000 },
      )
    } else {
      await expect(cities).toHaveCount(0)
    }
  }).toPass({ timeout: 20_000 })
})

test('a figure opens the inventory filtered to what it counts', async ({ page }) => {
  await signInAsMember(page)

  await page.getByRole('link', { name: /^Zimmetli/ }).click()

  await expect(page).toHaveURL(/\/envanter\?status=Assigned$/)
  await expect(page.getByRole('heading', { name: 'Envanter', level: 1 })).toBeVisible()
})
