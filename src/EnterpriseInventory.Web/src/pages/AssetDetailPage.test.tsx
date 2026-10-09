import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { AssetHistoryEntry } from '../inventory/assetsApi'
import { details } from '../test/assetData'
import { json, mockApi } from '../test/mockApi'
import { renderWithRouter } from '../test/renderWithRouter'

const csrf = { 'GET /api/auth/csrf': json(200, { token: 'csrf-token' }) }

const created: AssetHistoryEntry = {
  id: 1,
  action: 'Created',
  userName: 'ayse.yilmaz',
  timestamp: '2026-10-01T08:00:00Z',
  correlationId: 'c-1',
  oldValues: null,
  newValues: { assetCode: 'DMR-0001', status: 'Available', brandId: 1, brandName: 'Dell', locationName: null },
}

const statusChanged: AssetHistoryEntry = {
  id: 2,
  action: 'StatusChanged',
  userName: 'mehmet.admin',
  timestamp: '2026-10-05T09:30:00Z',
  correlationId: 'c-2',
  oldValues: { status: 'Available' },
  newValues: { status: 'Faulty' },
}

function historyOf(items: AssetHistoryEntry[], totalCount = items.length, page = 1) {
  return json(200, { items, page, pageSize: 10, totalCount, totalPages: Math.ceil(totalCount / 10) })
}

function field(label: string) {
  return screen.getByText(label, { selector: 'dt' }).nextElementSibling
}

describe('AssetDetailPage', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('shows the asset, where it is, who holds it and what happened to it', async () => {
    mockApi({
      'GET /api/assets/1': json(200, {
        ...details({ status: 'Assigned', description: 'Çantasıyla', updatedAt: '2026-10-05T09:30:00Z', updatedBy: 'mehmet.admin' }),
        activeAssignment: {
          id: 5,
          employeeId: 9,
          userName: 'ali.kaya',
          displayName: 'Ali Kaya',
          assignmentDescription: 'Dizüstü + çanta',
          assignedAt: '2026-10-02T10:00:00Z',
          assignedBy: 'ayse.yilmaz',
        },
      }),
      'GET /api/assets/1/history': historyOf([statusChanged, created]),
    })
    renderWithRouter('/envanter/1')

    expect(await screen.findByRole('heading', { level: 1, name: 'DMR-0001' })).toBeInTheDocument()
    expect(screen.getByText('Dizüstü · Dell Latitude 5440')).toBeInTheDocument()
    expect(field('Durum')).toHaveTextContent('Zimmetli')
    expect(field('Seri No')).toHaveTextContent('SN-100')
    expect(field('Lokasyon')).toHaveTextContent('Merkez Ofis')
    expect(field('Departman')).toHaveTextContent('Bilgi İşlem')
    expect(field('Açıklama')).toHaveTextContent('Çantasıyla')
    expect(field('Zimmetli Kişi')).toHaveTextContent('Ali Kaya (ali.kaya)')
    expect(field('Zimmet Tanımı')).toHaveTextContent('Dizüstü + çanta')
    expect(field('Son Değiştiren')).toHaveTextContent('mehmet.admin')

    const history = await screen.findByRole('list', { name: 'Demirbaş geçmişi' })
    const [first, second] = within(history).getAllByRole('listitem').filter((item) => item.parentElement === history)
    expect(first).toHaveTextContent('Durumu değişti')
    expect(first).toHaveTextContent('mehmet.admin')
    expect(first).toHaveTextContent('Durum: Boşta → Arızalı')
    expect(second).toHaveTextContent('Eklendi')
    expect(second).toHaveTextContent('Demirbaş kodu: DMR-0001')
    expect(second).toHaveTextContent('Marka: Dell')
    expect(second).not.toHaveTextContent('brandId')
  })

  it('writes a dash for empty values and says when the asset is not assigned', async () => {
    mockApi({
      'GET /api/assets/1': json(200, details({ computerName: null, location: null })),
      'GET /api/assets/1/history': historyOf([]),
    })
    renderWithRouter('/envanter/1')

    await screen.findByRole('heading', { level: 1, name: 'DMR-0001' })
    expect(within(field('Bilgisayar Adı') as HTMLElement).getByLabelText('Boş')).toBeInTheDocument()
    expect(within(field('Lokasyon') as HTMLElement).getByLabelText('Boş')).toBeInTheDocument()
    expect(screen.getByText('Bu demirbaş kimseye zimmetli değil.')).toBeInTheDocument()
    expect(await screen.findByText('Henüz geçmiş kaydı yok')).toBeInTheDocument()
  })

  it('shows the message the form left and clears it', async () => {
    mockApi({ 'GET /api/assets/1': json(200, details()), 'GET /api/assets/1/history': historyOf([created]) })
    const { router } = renderWithRouter({ pathname: '/envanter/1', state: { notice: 'Değişiklikler kaydedildi.' } })

    const notice = await screen.findByText('Değişiklikler kaydedildi.')
    fireEvent.click(within(notice.closest('[role="alert"]') as HTMLElement).getByRole('button', { name: /kapat/i }))

    await waitFor(() => expect(screen.queryByText('Değişiklikler kaydedildi.')).not.toBeInTheDocument())
    expect(router.state.location.state).toBeNull()
  })

  it('archives after confirmation, with the row version it showed', async () => {
    let archived = false
    const requests = mockApi({
      ...csrf,
      'GET /api/assets/1': () => json(200, details({ isArchived: archived })),
      'GET /api/assets/1/history': historyOf([created]),
      'DELETE /api/assets/1?rowVersion=AAAAAAAAB9E%3D': () => {
        archived = true
        return new Response(null, { status: 204 })
      },
    })
    renderWithRouter('/envanter/1')
    await screen.findByRole('heading', { level: 1, name: 'DMR-0001' })

    fireEvent.click(screen.getByRole('button', { name: 'Arşivle' }))
    const dialog = await screen.findByRole('dialog', { name: 'Demirbaşı arşivle' })
    expect(dialog).toHaveTextContent('DMR-0001 arşivlenecek.')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Arşivle' }))

    expect(await screen.findByText('DMR-0001 arşivlendi.')).toBeInTheDocument()
    expect(await screen.findByText('Bu demirbaş arşivlenmiş; yalnızca görüntülenebilir.')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Düzenle' })).not.toBeInTheDocument()
    // The dialog fades out first.
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Arşivle' })).not.toBeInTheDocument())
    expect(requests.filter((r) => r.method === 'DELETE').map((r) => r.path)).toEqual(['/api/assets/1?rowVersion=AAAAAAAAB9E%3D'])
  })

  it('does not archive an asset someone changed meanwhile and shows it as it is now', async () => {
    let version = 1
    mockApi({
      ...csrf,
      'GET /api/assets/1': () => json(200, details({ rowVersion: `v${version}`, computerName: `PC-V${version}` })),
      'GET /api/assets/1/history': historyOf([created]),
      'DELETE /api/assets/1': json(409, { title: 'Kayıt siz düzenlerken başka bir kullanıcı tarafından değiştirildi.', code: 'concurrency_conflict' }),
    })
    renderWithRouter('/envanter/1')
    await screen.findByRole('heading', { level: 1, name: 'DMR-0001' })
    version = 2

    fireEvent.click(screen.getByRole('button', { name: 'Arşivle' }))
    const dialog = await screen.findByRole('dialog', { name: 'Demirbaşı arşivle' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Arşivle' }))

    expect(await within(dialog).findByText('Kayıt siz bakarken değiştirildi.')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Arşivle' })).toBeDisabled()
    await waitFor(() => expect(field('Bilgisayar Adı')).toHaveTextContent('PC-V2'))
  })

  it('does not offer archiving while the asset is assigned', async () => {
    mockApi({
      'GET /api/assets/1': json(200, {
        ...details({ status: 'Assigned' }),
        activeAssignment: { id: 5, employeeId: 9, userName: 'ali.kaya', displayName: 'Ali Kaya', assignmentDescription: null, assignedAt: '2026-10-02T10:00:00Z', assignedBy: 'x' },
      }),
      'GET /api/assets/1/history': historyOf([]),
    })
    renderWithRouter('/envanter/1')

    await screen.findByRole('heading', { level: 1, name: 'DMR-0001' })
    expect(screen.getByRole('button', { name: 'Arşivle' })).toBeDisabled()
    expect(screen.getByRole('link', { name: 'Düzenle' })).toHaveAttribute('href', '/envanter/1/duzenle')
  })

  it('says so when the asset does not exist', async () => {
    mockApi({ 'GET /api/assets/7': json(404, { title: 'Demirbaş bulunamadı.', code: 'asset_not_found' }) })
    renderWithRouter('/envanter/7')

    expect(await screen.findByText('Demirbaş bulunamadı')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Envantere dön' })).toHaveAttribute('href', '/envanter')
  })

  it('offers a retry when the asset or its history cannot be loaded', async () => {
    let failing = true
    mockApi({
      'GET /api/assets/1': () => (failing ? json(500, { title: 'Hata' }) : json(200, details())),
      'GET /api/assets/1/history': json(500, { title: 'Hata' }),
    })
    renderWithRouter('/envanter/1')

    const alert = await screen.findByRole('alert', {}, { timeout: 4000 })
    expect(alert).toHaveTextContent('Demirbaş yüklenemedi')
    failing = false
    fireEvent.click(within(alert).getByRole('button', { name: 'Tekrar dene' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'DMR-0001' })).toBeInTheDocument()
    expect(await screen.findByText('Geçmiş yüklenemedi', {}, { timeout: 4000 })).toBeInTheDocument()
  })

  it('pages through a long history', async () => {
    const requests = mockApi({
      'GET /api/assets/1': json(200, details()),
      'GET /api/assets/1/history?page=1&pageSize=10': historyOf([statusChanged], 15, 1),
      'GET /api/assets/1/history?page=2&pageSize=10': historyOf([created], 15, 2),
    })
    renderWithRouter('/envanter/1')

    expect(await screen.findByText('15 kayıt · Sayfa 1 / 2')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Daha yeni' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Daha eski' }))

    expect(await screen.findByText('15 kayıt · Sayfa 2 / 2')).toBeInTheDocument()
    expect(await screen.findByText('Eklendi')).toBeInTheDocument()
    expect(requests.map((r) => r.path)).toContain('/api/assets/1/history?page=2&pageSize=10')
  })
})
