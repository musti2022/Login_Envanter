import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { formatDateTime } from '../inventory/labels'
import type { AssetSummary, AssetSummaryRow, AssignmentMovement, AssignmentReport } from '../reports/reportsApi'
import { lookupRoutes } from '../test/lookupData'
import { json, mockApi, type RecordedRequest } from '../test/mockApi'
import { renderWithRouter } from '../test/renderWithRouter'
import { choose } from '../test/select'
import { mockViewport } from '../test/viewport'

function row(key: string, name: string, counts: number[], inventoryFilter: Record<string, string> | null): AssetSummaryRow {
  const [totalCount, assignedCount, availableCount, faultyCount, retiredCount] = counts
  return { key, name, inventoryFilter, totalCount, assignedCount, availableCount, faultyCount, retiredCount }
}

const byLocation: AssetSummary = {
  rows: [
    row('none', 'Lokasyon belirtilmemiş', [1200, 800, 300, 90, 10], null),
    row('60', 'Merkez Ofis (İstanbul)', [50, 40, 10, 0, 0], { cityId: '6', locationId: '60' }),
  ],
  total: row('total', 'Toplam', [1250, 840, 310, 90, 10], null),
}

const byType: AssetSummary = {
  rows: [
    row('Laptop', 'Dizüstü', [700, 600, 100, 0, 0], { assetType: 'Laptop' }),
    row('Desktop', 'Masaüstü', [5, 1, 4, 0, 0], { assetType: 'Desktop' }),
  ],
  total: row('total', 'Toplam', [705, 601, 104, 0, 0], null),
}

const empty: AssetSummary = { rows: [], total: row('total', 'Toplam', [0, 0, 0, 0, 0], null) }

function movement(overrides: Partial<AssignmentMovement> = {}): AssignmentMovement {
  return {
    assignmentId: 7,
    movement: 'Assigned',
    at: '2026-10-31T20:59:00Z',
    by: 'admin.bir',
    assetId: 12,
    assetCode: 'DMR-0012',
    assetType: 'Laptop',
    brandName: 'Dell',
    modelName: 'Latitude 5440',
    serialNumber: 'SN-1',
    employeeUserName: 'ayse.yilmaz',
    employeeDisplayName: 'Ayşe Yılmaz',
    assignmentDescription: null,
    cityName: 'İstanbul',
    departmentName: 'Muhasebe',
    assetArchived: false,
    ...overrides,
  }
}

function reportOf(items: AssignmentMovement[], overrides: Partial<AssignmentReport> = {}): AssignmentReport {
  return { items, page: 1, pageSize: 25, totalCount: items.length, assignedCount: 4, returnedCount: 2, ...overrides }
}

const xlsx = () =>
  new Response('PK', {
    status: 200,
    headers: {
      'Content-Type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      'Content-Disposition': 'attachment; filename=rapor.xlsx',
    },
  })

function paths(requests: RecordedRequest[], prefix: string) {
  return requests.filter((r) => r.path.startsWith(prefix)).map((r) => r.path)
}

const { createObjectURL, revokeObjectURL } = URL

describe('ReportsPage', () => {
  let viewport: ReturnType<typeof mockViewport>

  beforeEach(() => {
    viewport = mockViewport(true)
    URL.createObjectURL = vi.fn(() => 'blob:test/1')
    URL.revokeObjectURL = vi.fn()
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
  })

  afterEach(() => {
    viewport.restore()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    vi.useRealTimers()
    URL.createObjectURL = createObjectURL
    URL.revokeObjectURL = revokeObjectURL
  })

  describe('inventory summary', () => {
    it('shows each group by status with a total, the groups linking to their assets in the inventory', async () => {
      const requests = mockApi({ ...lookupRoutes, 'GET /api/reports/asset-summary': json(200, byLocation) })
      renderWithRouter('/raporlar?groupBy=location&status=Assigned&status=Available')

      const table = await screen.findByRole('table', { name: 'Lokasyon bazında envanter özeti' })
      expect(paths(requests, '/api/reports')).toEqual([
        '/api/reports/asset-summary?status=Assigned&status=Available&groupBy=location',
      ])
      expect(
        within(table)
          .getAllByRole('row')
          .map((r) =>
            within(r)
              .getAllByRole(r.querySelector('th') ? 'columnheader' : 'cell')
              .map((c) => c.textContent),
          ),
      ).toEqual([
        ['Lokasyon', 'Toplam', 'Zimmetli', 'Boşta', 'Arızalı', 'Hurda'],
        ['Lokasyon belirtilmemiş', '1.200', '800', '300', '90', '10'],
        ['Merkez Ofis (İstanbul)', '50', '40', '10', '0', '0'],
        ['Toplam', '1.250', '840', '310', '90', '10'],
      ])
      // The report's filters and the row's own: the inventory lists the same assets the row counts.
      expect(within(table).getByRole('link', { name: 'Merkez Ofis (İstanbul)' })).toHaveAttribute(
        'href',
        '/envanter?status=Assigned&status=Available&cityId=6&locationId=60',
      )
      // The inventory cannot list "no location", so that row has no link.
      expect(within(table).getAllByRole('link')).toHaveLength(1)
      expect(screen.getByText(/2 lokasyon · Arşivlenmiş demirbaşlar sayılmaz/)).toBeInTheDocument()
      expect(screen.queryByLabelText('Arşivlenmişleri göster')).not.toBeInTheDocument()
    })

    it('regroups on request, and a type row opens that type only', async () => {
      const requests = mockApi({
        ...lookupRoutes,
        'GET /api/reports/asset-summary': json(200, byLocation),
        'GET /api/reports/asset-summary?assetType=Laptop&assetType=Desktop&groupBy=assetType': json(200, byType),
      })
      const { router } = renderWithRouter('/raporlar?assetType=Laptop&assetType=Desktop')
      await screen.findByRole('table')

      await choose('Gruplama', 'Tür')

      const table = await screen.findByRole('table', { name: 'Tür bazında envanter özeti' })
      expect(router.state.location.search).toBe('?assetType=Laptop&assetType=Desktop&groupBy=assetType')
      expect(paths(requests, '/api/reports').at(-1)).toBe(
        '/api/reports/asset-summary?assetType=Laptop&assetType=Desktop&groupBy=assetType',
      )
      expect(within(table).getByRole('link', { name: 'Dizüstü' })).toHaveAttribute('href', '/envanter?assetType=Laptop')
    })

    it('downloads the summary as it is grouped and filtered', async () => {
      const requests = mockApi({
        ...lookupRoutes,
        'GET /api/reports/asset-summary': json(200, byLocation),
        'GET /api/reports/asset-summary/export': xlsx(),
      })
      renderWithRouter('/raporlar?groupBy=location&cityId=6')
      await screen.findByRole('table')

      fireEvent.click(screen.getByRole('button', { name: "Excel'e aktar" }))

      await waitFor(() =>
        expect(paths(requests, '/api/reports/asset-summary/export')).toEqual([
          '/api/reports/asset-summary/export?cityId=6&groupBy=location',
        ]),
      )
    })

    it('says when the filters leave nothing, and clears them', async () => {
      const requests = mockApi({
        ...lookupRoutes,
        'GET /api/reports/asset-summary': json(200, byLocation),
        'GET /api/reports/asset-summary?status=Retired&groupBy=brand': json(200, empty),
      })
      renderWithRouter('/raporlar?status=Retired&groupBy=brand')

      expect(await screen.findByText('Filtrelerle eşleşen demirbaş yok')).toBeInTheDocument()
      expect(screen.getByRole('button', { name: "Excel'e aktar" })).toBeDisabled()
      fireEvent.click(screen.getAllByRole('button', { name: 'Filtreleri temizle' }).at(-1)!)

      await screen.findByRole('table')
      expect(paths(requests, '/api/reports').at(-1)).toBe('/api/reports/asset-summary?groupBy=brand')
    })

    it('shows a Turkish error with a retry button when the report cannot be read', async () => {
      let calls = 0
      mockApi({
        ...lookupRoutes,
        'GET /api/reports/asset-summary': () => (++calls <= 2 ? json(500, { title: 'Hata' }) : json(200, byLocation)),
      })
      renderWithRouter('/raporlar')

      // The query client retries once before giving up.
      const alert = await screen.findByRole('alert', {}, { timeout: 4000 })
      expect(alert).toHaveTextContent('Rapor alınamadı')
      fireEvent.click(within(alert).getByRole('button', { name: 'Tekrar dene' }))

      expect(await screen.findByRole('table', { name: 'Şehir bazında envanter özeti' })).toBeInTheDocument()
    })
  })

  describe('assignment movements', () => {
    it('lists the movements of the period with their totals, newest first', async () => {
      const requests = mockApi({
        ...lookupRoutes,
        'GET /api/reports/assignments': json(
          200,
          reportOf([
            movement(),
            movement({
              assignmentId: 3,
              movement: 'Returned',
              at: '2026-10-15T09:00:00Z',
              assetCode: 'DMR-0004',
              by: 'admin.iki',
              assetArchived: true,
            }),
          ]),
        ),
      })
      renderWithRouter('/raporlar/zimmet-hareketleri?from=2026-10-01&to=2026-10-31')

      const table = await screen.findByRole('table', { name: 'Zimmet hareketleri' })
      expect(screen.getByRole('tab', { name: 'Zimmet hareketleri' })).toHaveAttribute('aria-selected', 'true')
      expect(paths(requests, '/api/reports')).toEqual(['/api/reports/assignments?from=2026-10-01&to=2026-10-31'])
      expect(screen.getByText('1 Ekim 2026 – 31 Ekim 2026: 4 zimmet verildi, 2 iade alındı.')).toBeInTheDocument()
      const [first, second] = within(table).getAllByRole('row').slice(1)
      expect(
        within(first)
          .getAllByRole('cell')
          .map((c) => c.textContent),
      ).toEqual([
        formatDateTime('2026-10-31T20:59:00Z'),
        'Zimmet verildi',
        'DMR-0012Dizüstü',
        'Dell Latitude 5440SN-1',
        'Ayşe Yılmazayse.yilmaz',
        'İstanbulMuhasebe',
        'admin.bir',
      ])
      expect(within(first).getByRole('link', { name: 'DMR-0012' })).toHaveAttribute('href', '/envanter/12')
      expect(within(second).getAllByRole('cell')[1]).toHaveTextContent('İade alındı')
      expect(within(second).getAllByRole('cell')[2]).toHaveTextContent('DMR-0004Dizüstü · arşivlendi')
    })

    it('filters in Turkish: this month, a kind of movement, and a city; back to the first page', async () => {
      vi.useFakeTimers({ toFake: ['Date'] })
      vi.setSystemTime(new Date(2026, 9, 10, 9, 0))
      const requests = mockApi({ ...lookupRoutes, 'GET /api/reports/assignments': json(200, reportOf([movement()])) })
      const { router } = renderWithRouter('/raporlar/zimmet-hareketleri?page=3')
      await screen.findByRole('table', { name: 'Zimmet hareketleri' })

      fireEvent.click(screen.getByRole('button', { name: 'Bu ay' }))
      await waitFor(() => expect(router.state.location.search).toBe('?from=2026-10-01&to=2026-10-31'))
      await choose('Hareket', 'İade alındı')
      await choose('Şehir', 'İstanbul')

      await waitFor(() =>
        expect(paths(requests, '/api/reports').at(-1)).toBe(
          '/api/reports/assignments?from=2026-10-01&to=2026-10-31&movement=Returned&cityId=6',
        ),
      )
      fireEvent.click(screen.getByRole('button', { name: 'Geçen ay' }))
      await waitFor(() => expect(router.state.location.search).toBe('?from=2026-09-01&to=2026-09-30&movement=Returned&cityId=6'))
    })

    it('pages on the server and exports every page', async () => {
      const requests = mockApi({
        ...lookupRoutes,
        'GET /api/reports/assignments': json(200, reportOf([movement()], { totalCount: 60 })),
        'GET /api/reports/assignments/export': xlsx(),
      })
      renderWithRouter('/raporlar/zimmet-hareketleri?movement=Assigned')
      await screen.findByRole('table', { name: 'Zimmet hareketleri' })
      expect(screen.getByText('Tüm zamanlar: 4 zimmet verildi.')).toBeInTheDocument()

      fireEvent.click(screen.getByRole('button', { name: 'Sonraki sayfa' }))
      await waitFor(() =>
        expect(paths(requests, '/api/reports/assignments?').at(-1)).toBe('/api/reports/assignments?movement=Assigned&page=2'),
      )
      fireEvent.click(screen.getByRole('button', { name: "Excel'e aktar" }))

      await waitFor(() =>
        expect(paths(requests, '/api/reports/assignments/export')).toEqual(['/api/reports/assignments/export?movement=Assigned']),
      )
    })

    it('explains in Turkish when an export would be too large', async () => {
      mockApi({
        ...lookupRoutes,
        'GET /api/reports/assignments': json(200, reportOf([movement()])),
        'GET /api/reports/assignments/export': json(400, {
          title: 'Aktarılacak hareket sayısı sınırı aşıyor.',
          detail: 'Filtrelerle eşleşen 60.001 hareket var; bir dosyaya en fazla 50.000 hareket aktarılabilir.',
          code: 'export_too_large',
        }),
      })
      renderWithRouter('/raporlar/zimmet-hareketleri')
      await screen.findByRole('table', { name: 'Zimmet hareketleri' })

      fireEvent.click(screen.getByRole('button', { name: "Excel'e aktar" }))

      expect(await screen.findByText('Aktarılacak hareket sayısı sınırı aşıyor.')).toBeInTheDocument()
      expect(screen.getByText(/60.001 hareket var/)).toBeInTheDocument()
    })

    it('says when nothing moved in the period', async () => {
      mockApi({
        ...lookupRoutes,
        'GET /api/reports/assignments': json(200, reportOf([], { assignedCount: 0, returnedCount: 0 })),
      })
      renderWithRouter('/raporlar/zimmet-hareketleri?from=2026-01-01&to=2026-01-31')

      expect(await screen.findByText('Bu dönemde veya filtrelerle eşleşen hareket yok')).toBeInTheDocument()
    })
  })

  it('switches between the reports with the tabs', async () => {
    mockApi({
      ...lookupRoutes,
      'GET /api/reports/asset-summary': json(200, byLocation),
      'GET /api/reports/assignments': json(200, reportOf([movement()])),
    })
    const { router } = renderWithRouter('/raporlar')
    await screen.findByRole('table', { name: 'Şehir bazında envanter özeti' })

    fireEvent.click(screen.getByRole('tab', { name: 'Zimmet hareketleri' }))

    await screen.findByRole('table', { name: 'Zimmet hareketleri' })
    expect(router.state.location.pathname).toBe('/raporlar/zimmet-hareketleri')
  })
})
