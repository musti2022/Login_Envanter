import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { listItem, pageOf } from '../test/assetData'
import { json, mockApi, type RecordedRequest } from '../test/mockApi'
import { renderWithRouter } from '../test/renderWithRouter'

const rows = [
  listItem(),
  listItem({
    id: 2,
    assetCode: 'DMR-0002',
    computerName: null,
    serialNumber: null,
    status: 'Available',
    locationName: null,
    assignedUserName: null,
    assignedDisplayName: null,
    assignmentDescription: null,
  }),
]

function listRequests(requests: RecordedRequest[]) {
  return requests.filter((r) => r.path.startsWith('/api/assets')).map((r) => r.path)
}

function table() {
  return screen.getByRole('table', { name: 'Demirbaş listesi' })
}

function headers() {
  return within(table()).getAllByRole('columnheader').map((header) => header.textContent)
}

describe('InventoryPage', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('shows the columns the users asked for, with the API rows', async () => {
    mockApi({ 'GET /api/assets': json(200, pageOf(rows)) })
    renderWithRouter('/envanter')

    await screen.findByRole('table', { name: 'Demirbaş listesi' })
    expect(headers()).toEqual([
      'Demirbaş Kodu',
      'Kullanıcı Adı',
      'Bilgisayar Adı',
      'Marka',
      'Model',
      'Seri No',
      'Zimmet Tanımı',
      'Lokasyon/Şehir',
      'Lokasyon/Departman',
      'Durum',
      'İşlemler',
    ])
    const [, assigned, free] = within(table()).getAllByRole('row')
    expect(within(assigned).getAllByRole('cell').map((cell) => cell.textContent)).toEqual([
      'DMR-0001',
      'Ali Kayaali.kaya',
      'PC-IST-01',
      'Dell',
      'Latitude 5440',
      'SN-100',
      'Dizüstü + çanta',
      'İstanbulMerkez Ofis',
      'Bilgi İşlem',
      'Zimmetli',
      '',
    ])
    expect(within(free).getAllByLabelText('Boş')).toHaveLength(4)
    expect(within(free).getByText('Boşta')).toBeInTheDocument()
    expect(within(assigned).getByRole('link', { name: 'DMR-0001' })).toHaveAttribute('href', '/envanter/1')
    expect(within(assigned).getByRole('link', { name: 'DMR-0001 detayı' })).toHaveAttribute('href', '/envanter/1')
  })

  it('asks the API for what the address says', async () => {
    const requests = mockApi({ 'GET /api/assets': json(200, pageOf(rows, { page: 2, pageSize: 10, totalCount: 12 })) })
    renderWithRouter('/envanter?sortBy=cityName&sortDirection=desc&page=2&pageSize=10&status=Assigned')

    await screen.findByRole('table', { name: 'Demirbaş listesi' })
    expect(listRequests(requests)).toEqual(['/api/assets?status=Assigned&sortBy=cityName&sortDirection=desc&page=2&pageSize=10'])
    expect(screen.getByRole('columnheader', { name: /Lokasyon\/Şehir/ })).toHaveAttribute('aria-sort', 'descending')
    expect(screen.getByText('11–12 / 12')).toBeInTheDocument()
  })

  it('sorts on the server when a column header is clicked, starting again from the first page', async () => {
    const requests = mockApi({ 'GET /api/assets': json(200, pageOf(rows, { totalCount: 60 })) })
    const { router } = renderWithRouter('/envanter?page=2')
    await screen.findByRole('table', { name: 'Demirbaş listesi' })

    fireEvent.click(screen.getByRole('button', { name: 'Marka' }))
    await waitFor(() => expect(router.state.location.search).toBe('?sortBy=brandName'))
    await waitFor(() => expect(screen.getByRole('columnheader', { name: /Marka/ })).toHaveAttribute('aria-sort', 'ascending'))

    fireEvent.click(screen.getByRole('button', { name: 'Marka' }))
    await waitFor(() => expect(router.state.location.search).toBe('?sortBy=brandName&sortDirection=desc'))

    await waitFor(() =>
      expect(listRequests(requests)).toEqual([
        '/api/assets?page=2',
        '/api/assets?sortBy=brandName',
        '/api/assets?sortBy=brandName&sortDirection=desc',
      ]),
    )
    expect(screen.getByRole('columnheader', { name: 'Zimmet Tanımı' })).not.toHaveAttribute('aria-sort')
    expect(within(screen.getByRole('columnheader', { name: 'Zimmet Tanımı' })).queryByRole('button')).not.toBeInTheDocument()
  })

  it('pages on the server and keeps the page size in the address', async () => {
    const requests = mockApi({ 'GET /api/assets': json(200, pageOf(rows, { totalCount: 60 })) })
    const { router } = renderWithRouter('/envanter')
    await screen.findByRole('table', { name: 'Demirbaş listesi' })
    expect(screen.getByText('1–25 / 60')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Sonraki sayfa' }))
    await waitFor(() => expect(router.state.location.search).toBe('?page=2'))

    fireEvent.mouseDown(screen.getByRole('combobox', { name: /Sayfa başına kayıt/ }))
    fireEvent.click(await screen.findByRole('option', { name: '50' }))
    await waitFor(() => expect(router.state.location.search).toBe('?pageSize=50'))

    await waitFor(() => expect(listRequests(requests)).toEqual(['/api/assets', '/api/assets?page=2', '/api/assets?pageSize=50']))
  })

  it('hides and shows columns from the column menu', async () => {
    mockApi({ 'GET /api/assets': json(200, pageOf(rows)) })
    renderWithRouter('/envanter')
    await screen.findByRole('table', { name: 'Demirbaş listesi' })

    fireEvent.click(screen.getByRole('button', { name: 'Sütunlar' }))
    fireEvent.click(screen.getByRole('menuitemcheckbox', { name: 'Seri No' }))
    fireEvent.click(screen.getByRole('menuitemcheckbox', { name: 'Tür' }))
    expect(screen.getByRole('menuitemcheckbox', { name: 'Seri No' })).toHaveAttribute('aria-checked', 'false')
    expect(screen.getByRole('menuitemcheckbox', { name: 'Tür' })).toHaveAttribute('aria-checked', 'true')
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' })

    await waitFor(() => expect(headers()).not.toContain('Seri No'))
    expect(headers()).toContain('Tür')
    expect(within(table()).getAllByText('Dizüstü')).toHaveLength(2)
  })

  it('shows an empty state when there are no assets', async () => {
    mockApi({ 'GET /api/assets': json(200, pageOf([])) })
    renderWithRouter('/envanter')

    expect(await screen.findByText('Henüz demirbaş yok')).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('offers the first page when the page asked for is past the end', async () => {
    const requests = mockApi({
      'GET /api/assets?page=9': json(200, pageOf([], { page: 9, totalCount: 3 })),
      'GET /api/assets': json(200, pageOf(rows, { totalCount: 3 })),
    })
    renderWithRouter('/envanter?page=9')

    fireEvent.click(await screen.findByRole('button', { name: 'İlk sayfaya dön' }))

    await screen.findByRole('table', { name: 'Demirbaş listesi' })
    expect(listRequests(requests)).toEqual(['/api/assets?page=9', '/api/assets'])
  })

  it('shows a Turkish error with a retry button when the list cannot be loaded', async () => {
    let calls = 0
    mockApi({ 'GET /api/assets': () => (++calls <= 2 ? json(503, { title: 'Hizmet kullanılamıyor.' }) : json(200, pageOf(rows))) })
    renderWithRouter('/envanter')

    const alert = await screen.findByRole('alert', {}, { timeout: 4000 })
    expect(alert).toHaveTextContent('Demirbaşlar yüklenemedi')
    fireEvent.click(within(alert).getByRole('button', { name: 'Tekrar dene' }))

    expect(await screen.findByRole('table', { name: 'Demirbaş listesi' })).toBeInTheDocument()
  })

  it('links to adding an asset and to editing each asset that is not archived', async () => {
    mockApi({ 'GET /api/assets': json(200, pageOf([listItem(), listItem({ id: 3, assetCode: 'DMR-0003', isArchived: true })])) })
    renderWithRouter('/envanter')

    await screen.findByRole('table', { name: 'Demirbaş listesi' })
    expect(screen.getByRole('link', { name: 'Yeni Demirbaş' })).toHaveAttribute('href', '/envanter/yeni')
    expect(screen.getByRole('link', { name: 'DMR-0001 düzenle' })).toHaveAttribute('href', '/envanter/1/duzenle')
    expect(screen.queryByRole('link', { name: 'DMR-0003 düzenle' })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'DMR-0003 detayı' })).toHaveAttribute('href', '/envanter/3')
  })
})
