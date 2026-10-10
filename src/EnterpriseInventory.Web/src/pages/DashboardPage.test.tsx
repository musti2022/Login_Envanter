import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { DashboardStatistics } from '../dashboard/dashboardApi'
import { formatDateTime } from '../inventory/labels'
import { json, mockApi } from '../test/mockApi'
import { renderWithRouter } from '../test/renderWithRouter'

const statistics: DashboardStatistics = {
  totalCount: 1250,
  assignedCount: 900,
  availableCount: 300,
  faultyCount: 40,
  retiredCount: 10,
  archivedCount: 7,
  byCity: {
    items: [
      { id: 1, name: 'İstanbul', count: 800, assignedCount: 600 },
      { id: 2, name: 'Ankara', count: 300, assignedCount: 200 },
    ],
    otherGroupCount: 3,
    otherCount: 150,
    otherAssignedCount: 100,
  },
  byDepartment: {
    items: [{ id: 5, name: 'Bilgi İşlem', count: 1250, assignedCount: 900 }],
    otherGroupCount: 0,
    otherCount: 0,
    otherAssignedCount: 0,
  },
  byType: [
    { assetType: 'Laptop', count: 1000 },
    { assetType: 'Monitor', count: 250 },
  ],
  byBrand: {
    items: [
      { id: 3, name: 'Dell', count: 700, assignedCount: 500 },
      { id: 4, name: 'HP', count: 500, assignedCount: 400 },
    ],
    otherGroupCount: 2,
    otherCount: 50,
    otherAssignedCount: 0,
  },
  monthlyMovements: [
    ['2025-11', 4, 1],
    ['2025-12', 0, 0],
    ['2026-01', 12, 3],
    ['2026-02', 0, 2],
    ['2026-03', 7, 7],
    ['2026-04', 1, 0],
    ['2026-05', 0, 0],
    ['2026-06', 0, 0],
    ['2026-07', 0, 0],
    ['2026-08', 3, 0],
    ['2026-09', 2, 9],
    ['2026-10', 5, 3],
  ].map(([month, assignedCount, returnedCount]) => ({
    month: month as string,
    assignedCount: assignedCount as number,
    returnedCount: returnedCount as number,
  })),
  recentActivity: [
    { id: 30, assetId: 12, assetCode: 'DMR-0012', action: 'Archived', userName: 'ayse.admin', timestamp: '2026-10-09T08:15:00Z' },
    { id: 29, assetId: 9, assetCode: null, action: 'Created', userName: 'mehmet.admin', timestamp: '2026-10-09T08:00:00Z' },
  ],
}

const empty: DashboardStatistics = {
  totalCount: 0,
  assignedCount: 0,
  availableCount: 0,
  faultyCount: 0,
  retiredCount: 0,
  archivedCount: 0,
  byCity: { items: [], otherGroupCount: 0, otherCount: 0, otherAssignedCount: 0 },
  byDepartment: { items: [], otherGroupCount: 0, otherCount: 0, otherAssignedCount: 0 },
  byType: [],
  byBrand: { items: [], otherGroupCount: 0, otherCount: 0, otherAssignedCount: 0 },
  monthlyMovements: statistics.monthlyMovements.map((m) => ({ ...m, assignedCount: 0, returnedCount: 0 })),
  recentActivity: [],
}

function kpiCard(label: string) {
  return screen.getByText(label, { selector: 'a *' }).closest('a') as HTMLElement
}

describe('DashboardPage', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('shows the figures from the API, each card linking to the matching inventory', async () => {
    mockApi({ 'GET /api/dashboard/statistics': json(200, statistics) })
    renderWithRouter('/')

    await screen.findByText('Toplam Demirbaş')
    expect(within(kpiCard('Toplam Demirbaş')).getByText('1.250')).toBeInTheDocument()
    expect(within(kpiCard('Zimmetli')).getByText('900')).toBeInTheDocument()
    expect(within(kpiCard('Boşta')).getByText('300')).toBeInTheDocument()
    expect(within(kpiCard('Arızalı')).getByText('40')).toBeInTheDocument()
    expect(kpiCard('Toplam Demirbaş')).toHaveAttribute('href', '/envanter')
    expect(kpiCard('Zimmetli')).toHaveAttribute('href', '/envanter?status=Assigned')
    expect(kpiCard('Boşta')).toHaveAttribute('href', '/envanter?status=Available')
    expect(kpiCard('Arızalı')).toHaveAttribute('href', '/envanter?status=Faulty')
    expect(screen.getByText(/Hurda: 10 · Arşivlenmiş: 7/)).toBeInTheDocument()
  })

  it('lists the largest cities and departments with their assets and assigned assets, linking to the filtered inventory, and the rest together', async () => {
    mockApi({ 'GET /api/dashboard/statistics': json(200, statistics) })
    renderWithRouter('/')

    const cities = await screen.findByRole('list', { name: 'Şehirlere göre demirbaş sayısı' })
    expect(
      within(cities)
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual(['İstanbul600 zimmetli · 800', 'Ankara200 zimmetli · 300', 'Diğer 3 şehir100 zimmetli · 150'])
    expect(within(cities).getByRole('link', { name: 'İstanbul' })).toHaveAttribute('href', '/envanter?cityId=1')
    expect(within(cities).getAllByRole('link')).toHaveLength(2)
    const departments = screen.getByRole('list', { name: 'Departmanlara göre demirbaş sayısı' })
    expect(within(departments).getByRole('listitem')).toHaveTextContent('Bilgi İşlem900 zimmetli · 1.250')
    expect(within(departments).getByRole('link', { name: 'Bilgi İşlem' })).toHaveAttribute('href', '/envanter?departmentId=5')
  })

  it('lists types in Turkish and the largest brands, with the other brands together', async () => {
    mockApi({ 'GET /api/dashboard/statistics': json(200, statistics) })
    renderWithRouter('/')

    const types = await screen.findByRole('list', { name: 'Türlere göre demirbaş sayısı' })
    expect(
      within(types)
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual(['Dizüstü1.000', 'Monitör250'])
    expect(within(types).getByRole('link', { name: 'Dizüstü' })).toHaveAttribute('href', '/envanter?assetType=Laptop')
    const brands = screen.getByRole('list', { name: 'Markalara göre demirbaş sayısı' })
    expect(
      within(brands)
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual(['Dell700', 'HP500', 'Diğer 2 marka50'])
    expect(within(brands).getByRole('link', { name: 'HP' })).toHaveAttribute('href', '/envanter?brandId=4')
    expect(within(brands).getAllByRole('link')).toHaveLength(2)
  })

  it('shows twelve months of assignments and returns as bars, and as a table on request', async () => {
    mockApi({ 'GET /api/dashboard/statistics': json(200, statistics) })
    renderWithRouter('/')

    const chart = await screen.findByRole('list', { name: 'Aylık zimmet hareketleri' })
    const months = within(chart).getAllByRole('listitem')
    expect(months).toHaveLength(12)
    expect(months[0]).toHaveAccessibleName('Kasım 2025: 4 zimmet, 1 iade')
    expect(months[11]).toHaveAccessibleName('Ekim 2026: 5 zimmet, 3 iade')
    expect(months[11]).toHaveAttribute('tabindex', '0')

    fireEvent.click(screen.getByRole('button', { name: 'Tablo olarak göster' }))
    const table = screen.getByRole('table', { name: 'Aylık zimmet hareketleri' })
    const rows = within(table).getAllByRole('row')
    expect(rows).toHaveLength(13)
    expect(
      within(rows[0])
        .getAllByRole('columnheader')
        .map((h) => h.textContent),
    ).toEqual(['Ay', 'Zimmet verildi', 'İade alındı'])
    expect(
      within(rows[3])
        .getAllByRole('cell')
        .map((c) => c.textContent),
    ).toEqual(['Ocak 2026', '12', '3'])
    fireEvent.click(screen.getByRole('button', { name: 'Grafik olarak göster' }))
    expect(screen.getByRole('list', { name: 'Aylık zimmet hareketleri' })).toBeInTheDocument()
  })

  it('shows recent activity in Turkish with links to the assets', async () => {
    mockApi({ 'GET /api/dashboard/statistics': json(200, statistics) })
    renderWithRouter('/')

    const activity = await screen.findByRole('list', { name: 'Son işlemler' })
    const [archived, created] = within(activity).getAllByRole('listitem')
    expect(archived).toHaveTextContent('DMR-0012 arşivlendi')
    expect(archived).toHaveTextContent(`ayse.admin · ${formatDateTime('2026-10-09T08:15:00Z')}`)
    expect(within(archived).getByRole('link', { name: 'DMR-0012' })).toHaveAttribute('href', '/envanter/12')
    expect(created).toHaveTextContent('Silinmiş kayıt eklendi')
    expect(within(created).queryByRole('link')).not.toBeInTheDocument()
  })

  it('shows zeros and empty states for an empty inventory', async () => {
    mockApi({ 'GET /api/dashboard/statistics': json(200, empty) })
    renderWithRouter('/')

    await waitFor(() => expect(within(kpiCard('Toplam Demirbaş')).getByText('0')).toBeInTheDocument())
    expect(screen.getAllByText('Henüz demirbaş yok')).toHaveLength(4)
    expect(screen.getByText('Henüz işlem yok')).toBeInTheDocument()
    expect(screen.getByText('Son 12 ayda zimmet verilmedi ve iade alınmadı.')).toBeInTheDocument()
  })

  it('shows a Turkish error with a retry button when the figures cannot be loaded', async () => {
    let calls = 0
    const requests = mockApi({
      'GET /api/dashboard/statistics': () =>
        ++calls <= 2 ? json(500, { title: 'Beklenmeyen bir hata oluştu.' }) : json(200, statistics),
    })
    renderWithRouter('/')

    // The query client retries once before giving up.
    const alert = await screen.findByRole('alert', {}, { timeout: 4000 })
    expect(alert).toHaveTextContent('İstatistikler yüklenemedi')
    within(alert).getByRole('button', { name: 'Tekrar dene' }).click()

    await screen.findByText('Toplam Demirbaş')
    expect(within(kpiCard('Toplam Demirbaş')).getByText('1.250')).toBeInTheDocument()
    expect(requests.filter((r) => r.path === '/api/dashboard/statistics').length).toBe(3)
  })
})
