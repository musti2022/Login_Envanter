import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { AuditLogEntry } from '../audit/auditApi'
import type { PagedResult } from '../inventory/assetsApi'
import { json, mockApi, type RecordedRequest } from '../test/mockApi'
import { renderWithRouter } from '../test/renderWithRouter'
import { choose } from '../test/select'
import { mockViewport } from '../test/viewport'

function entry(overrides: Partial<AuditLogEntry> = {}): AuditLogEntry {
  return {
    id: 41,
    entityName: 'Asset',
    entityId: '7',
    entityLabel: 'DMR-0007',
    action: 'Updated',
    userName: 'ayse.admin',
    timestamp: '2026-10-09T08:12:40.123+00:00',
    correlationId: '3f6c0b9e4a2d4c55a8a0f1d7c2e9b411',
    oldValues: { computerName: 'PC-TEST', description: 'Eski açıklama', serialNumber: 'SN-1', modelId: 3, modelName: 'Latitude' },
    newValues: { computerName: 'PC-YENI', description: null, serialNumber: 'SN-2', modelId: 4, modelName: 'Precision' },
    ...overrides,
  }
}

function pageOf(items: AuditLogEntry[]): PagedResult<AuditLogEntry> {
  return { items, page: 1, pageSize: 25, totalCount: items.length, totalPages: items.length === 0 ? 0 : 1 }
}

function auditRequests(requests: RecordedRequest[]) {
  return requests.filter((r) => r.path.startsWith('/api/audit-logs')).map((r) => r.path)
}

function setup(path: string, entries: AuditLogEntry[] = [entry()]) {
  const requests = mockApi({ 'GET /api/audit-logs': json(200, pageOf(entries)) })
  return { requests, ...renderWithRouter(path) }
}

describe('audit log page', () => {
  let viewport: ReturnType<typeof mockViewport>

  beforeEach(() => {
    viewport = mockViewport(true)
  })

  afterEach(() => {
    viewport.restore()
    vi.unstubAllGlobals()
  })

  it('lists who changed which record when, with the values before and after', async () => {
    setup('/denetim-gecmisi')

    const table = await screen.findByRole('table', { name: 'Denetim kayıtları' })
    const row = within(table).getAllByRole('row')[1]
    expect(within(row).getByText('ayse.admin')).toBeInTheDocument()
    expect(within(row).getByText('Güncellendi')).toBeInTheDocument()
    expect(within(row).getByRole('link', { name: 'DMR-0007' })).toHaveAttribute('href', '/envanter/7')
    expect(within(row).getByText('Bilgisayar adı: PC-TEST → PC-YENI')).toBeInTheDocument()
    expect(within(row).getByText('Seri no: SN-1 → SN-2')).toBeInTheDocument()
    expect(within(row).getByText('Model: Latitude → Precision')).toBeInTheDocument()
    expect(within(row).getByText('+1 alan daha')).toBeInTheDocument()
  })

  it('asks the API for the first page and shows the paging of its answer', async () => {
    const { requests } = setup('/denetim-gecmisi')
    await screen.findByRole('table', { name: 'Denetim kayıtları' })

    expect(auditRequests(requests)).toEqual(['/api/audit-logs?page=1&pageSize=25'])
    expect(screen.getByText('1–1 / 1')).toBeInTheDocument()
  })

  it('filters by kind of record, action and user, from the first page, and keeps the filters in the address', async () => {
    const { requests, router } = setup('/denetim-gecmisi?page=3')
    await screen.findByRole('table', { name: 'Denetim kayıtları' })

    await choose('Kayıt türü', 'Demirbaş')
    await choose('İşlem', 'Zimmetlendi', 'Güncellendi')
    fireEvent.change(screen.getByRole('textbox', { name: 'Kullanıcı' }), { target: { value: 'ayse' } })

    await waitFor(() =>
      expect(auditRequests(requests).at(-1)).toBe(
        '/api/audit-logs?entityName=Asset&action=Updated&action=Assigned&userName=ayse&page=1&pageSize=25',
      ),
    )
    expect(router.state.location.search).toBe('?entityName=Asset&action=Updated&action=Assigned&userName=ayse')
  })

  it('turns the chosen days into a window from the start of the first to the end of the last, in local time', async () => {
    const { requests, router } = setup('/denetim-gecmisi')
    await screen.findByRole('table', { name: 'Denetim kayıtları' })

    fireEvent.change(screen.getByLabelText('Başlangıç tarihi'), { target: { value: '2026-10-01' } })
    fireEvent.change(screen.getByLabelText('Bitiş tarihi'), { target: { value: '2026-10-05' } })

    const from = new Date(2026, 9, 1).toISOString()
    const to = new Date(2026, 9, 6).toISOString()
    await waitFor(() =>
      expect(auditRequests(requests).at(-1)).toBe(
        `/api/audit-logs?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&page=1&pageSize=25`,
      ),
    )
    expect(router.state.location.search).toBe('?from=2026-10-01&to=2026-10-05')
  })

  it('searches asset codes only where they apply', async () => {
    const { requests } = setup('/denetim-gecmisi?entityName=Brand&assetCode=PC')
    await screen.findByRole('table', { name: 'Denetim kayıtları' })

    expect(auditRequests(requests)).toEqual(['/api/audit-logs?entityName=Brand&page=1&pageSize=25'])
    expect(screen.getByRole('textbox', { name: 'Demirbaş kodu' })).toBeDisabled()
    expect(screen.getByText('Yalnızca demirbaş kayıtlarında')).toBeInTheDocument()
  })

  it('opens the records of one asset from its detail page link and lets the user widen the list', async () => {
    const { requests } = setup('/denetim-gecmisi?entityName=Asset&entityId=7')
    await screen.findByRole('table', { name: 'Denetim kayıtları' })
    expect(auditRequests(requests)).toEqual(['/api/audit-logs?entityName=Asset&entityId=7&page=1&pageSize=25'])

    fireEvent.click(screen.getByRole('button', { name: 'Demirbaş no: 7 filtresini kaldır' }))

    await waitFor(() => expect(auditRequests(requests).at(-1)).toBe('/api/audit-logs?entityName=Asset&page=1&pageSize=25'))
  })

  it('shows every field of a record before and after, and every record of the same request', async () => {
    const { requests } = setup('/denetim-gecmisi?userName=ayse')
    await screen.findByRole('table', { name: 'Denetim kayıtları' })

    fireEvent.click(screen.getByRole('button', { name: 'Güncellendi, DMR-0007: ayrıntı' }))

    const dialog = await screen.findByRole('dialog', { name: 'Güncellendi: Demirbaş DMR-0007' })
    expect(within(dialog).getByText('3f6c0b9e4a2d4c55a8a0f1d7c2e9b411')).toBeInTheDocument()
    const values = within(dialog).getByRole('table', { name: 'Önceki ve yeni değerler' })
    const rows = within(values)
      .getAllByRole('row')
      .slice(1)
      .map((row) => [...row.querySelectorAll('th, td')].map((cell) => cell.textContent))
    expect(rows).toEqual([
      ['Bilgisayar adı', 'PC-TEST', 'PC-YENI'],
      ['Seri no', 'SN-1', 'SN-2'],
      ['Model', 'Latitude', 'Precision'],
      ['Açıklama', 'Eski açıklama', '—'],
    ])

    fireEvent.click(within(dialog).getByRole('button', { name: 'Bu işlemin tüm kayıtları' }))

    await waitFor(() =>
      expect(auditRequests(requests).at(-1)).toBe(
        '/api/audit-logs?correlationId=3f6c0b9e4a2d4c55a8a0f1d7c2e9b411&page=1&pageSize=25',
      ),
    )
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('names sign-ins by the administrator and records that are gone by their number', async () => {
    setup('/denetim-gecmisi', [
      entry({
        id: 50,
        entityName: 'AdminUser',
        entityId: '3',
        entityLabel: 'dev.admin',
        action: 'SignedIn',
        userName: 'dev.admin',
        oldValues: null,
        newValues: { SamAccountName: 'dev.admin', DisplayName: 'Geliştirici Yönetici', SessionId: 9, ClientAddress: '10.0.0.5' },
      }),
      entry({
        id: 49,
        entityId: '12',
        entityLabel: null,
        action: 'Created',
        oldValues: null,
        newValues: { assetCode: 'ESKI-1' },
      }),
    ])

    const table = await screen.findByRole('table', { name: 'Denetim kayıtları' })
    const [signIn, gone] = within(table).getAllByRole('row').slice(1)
    expect(within(signIn).getByText('Giriş yaptı')).toBeInTheDocument()
    expect(within(signIn).getByText('Yönetici')).toBeInTheDocument()
    expect(within(signIn).getByText('IP adresi: 10.0.0.5')).toBeInTheDocument()
    expect(within(signIn).queryByText(/SessionId/)).not.toBeInTheDocument()
    expect(within(gone).getByText('#12 (artık yok)')).toBeInTheDocument()
    expect(within(gone).queryByRole('link')).not.toBeInTheDocument()
  })

  it('folds the filters under a button on a phone, saying how many are on', async () => {
    viewport.setDesktop(false)
    setup('/denetim-gecmisi?userName=ayse')
    await screen.findByRole('table', { name: 'Denetim kayıtları' })
    expect(screen.queryByRole('combobox', { name: 'Kayıt türü' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Filtreler, 1 filtre açık' }))

    expect(await screen.findByRole('combobox', { name: 'Kayıt türü' })).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Kullanıcı' })).toHaveValue('ayse')
  })

  it('says when no record matches the filters and clears them', async () => {
    const { requests } = setup('/denetim-gecmisi?userName=kimse', [])

    expect(await screen.findByText('Filtrelerle eşleşen kayıt yok')).toBeInTheDocument()
    fireEvent.click(screen.getAllByRole('button', { name: 'Filtreleri temizle' }).at(-1)!)

    await waitFor(() => expect(auditRequests(requests).at(-1)).toBe('/api/audit-logs?page=1&pageSize=25'))
  })

  it('offers to try again when the records cannot be read', async () => {
    mockApi({ 'GET /api/audit-logs': json(500, { title: 'Beklenmeyen bir hata oluştu.', status: 500 }) })
    renderWithRouter('/denetim-gecmisi')

    // One retry first, as for every query.
    expect(await screen.findByText('Denetim kayıtları yüklenemedi', {}, { timeout: 4000 })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Tekrar dene' })).toBeInTheDocument()
  })
})
