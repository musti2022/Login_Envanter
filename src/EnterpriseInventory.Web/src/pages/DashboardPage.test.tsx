import { screen, waitFor, within } from '@testing-library/react'
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
  byCity: [
    { id: 1, name: 'İstanbul', count: 800 },
    { id: 2, name: 'Ankara', count: 450 },
  ],
  byDepartment: [{ id: 5, name: 'Bilgi İşlem', count: 1250 }],
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
  byCity: [],
  byDepartment: [],
  recentActivity: [],
}

function kpiCard(label: string) {
  return screen.getByText(label).closest('a') as HTMLElement
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

  it('lists cities and departments with their counts, largest first', async () => {
    mockApi({ 'GET /api/dashboard/statistics': json(200, statistics) })
    renderWithRouter('/')

    const cities = await screen.findByRole('list', { name: 'Şehirlere göre demirbaş sayısı' })
    expect(within(cities).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['İstanbul800', 'Ankara450'])
    const departments = screen.getByRole('list', { name: 'Departmanlara göre demirbaş sayısı' })
    expect(within(departments).getByText('Bilgi İşlem')).toBeInTheDocument()
    expect(within(departments).getByText('1.250')).toBeInTheDocument()
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
    expect(screen.getAllByText('Henüz demirbaş yok')).toHaveLength(2)
    expect(screen.getByText('Henüz işlem yok')).toBeInTheDocument()
  })

  it('shows a Turkish error with a retry button when the figures cannot be loaded', async () => {
    let calls = 0
    const requests = mockApi({
      'GET /api/dashboard/statistics': () => (++calls <= 2 ? json(500, { title: 'Beklenmeyen bir hata oluştu.' }) : json(200, statistics)),
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
