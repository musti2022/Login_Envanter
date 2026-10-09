import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { dashboardQueryKey } from '../dashboard/dashboardApi'
import { details, listItem, pageOf } from '../test/assetData'
import { fakeHub } from '../test/fakeHub'
import { lookupRoutes } from '../test/lookupData'
import { json, mockApi, type RecordedRequest } from '../test/mockApi'
import { renderWithRouter } from '../test/renderWithRouter'

const emptyPage = json(200, { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 })
const pageRoutes = { 'GET /api/assets/1/history': emptyPage, 'GET /api/assets/1/assignments': emptyPage }

function liveStatus() {
  return screen.getByRole('status', { name: 'Canlı güncelleme durumu' })
}

function field(label: string) {
  return screen.getByText(label, { selector: 'dt' }).nextElementSibling
}

function gets(requests: RecordedRequest[], path: string) {
  return requests.filter((r) => r.method === 'GET' && r.path.split('?')[0] === path)
}

/** The hub announces a change, as SignalR would deliver it. */
function announce(event: string, assetId: number) {
  act(() => fakeHub.latest().emit(event, { assetId, occurredAt: '2026-10-09T12:00:00Z' }))
}

describe('Live updates', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('connect once the app is shown and say so in the header', async () => {
    let connect!: () => void
    fakeHub.configure = (connection) => {
      connection.startResult = () => new Promise<void>((resolve) => (connect = resolve))
    }
    mockApi({ 'GET /api/assets': json(200, pageOf([])) })
    renderWithRouter('/envanter')

    expect(liveStatus()).toHaveTextContent('Bağlanıyor')
    act(() => connect())

    await waitFor(() => expect(liveStatus()).toHaveTextContent('Canlı'))
    expect(fakeHub.connections).toHaveLength(1)
  })

  it('show a detail page as another user changed it, fetched again in the background', async () => {
    let version = 1
    const requests = mockApi({
      ...pageRoutes,
      'GET /api/assets/1': () => json(200, details({ computerName: `PC-V${version}`, rowVersion: `AAAAAAAAB9${version}=` })),
    })
    renderWithRouter('/envanter/1')
    await waitFor(() => expect(field('Bilgisayar Adı')).toHaveTextContent('PC-V1'))

    version = 2
    announce('AssetUpdated', 1)

    await waitFor(() => expect(field('Bilgisayar Adı')).toHaveTextContent('PC-V2'))
    const reads = gets(requests, '/api/assets/1')
    expect(reads.map((r) => r.headers['x-background-request'] ?? 'user')).toEqual(['user', '1'])
    // The history and assignments of the asset are fetched again too.
    expect(gets(requests, '/api/assets/1/history')).toHaveLength(2)
    expect(gets(requests, '/api/assets/1/assignments')).toHaveLength(2)
  })

  it.each([
    ['AssetCreated'],
    ['AssetUpdated'],
    ['AssetArchived'],
    ['AssetAssigned'],
    ['AssetReturned'],
    ['AssetLocationChanged'],
  ])('refresh the inventory list and the dashboard on %s of any asset', async (event) => {
    let rows = [listItem({ id: 1, assetCode: 'DMR-0001' })]
    const requests = mockApi({ 'GET /api/assets': () => json(200, pageOf(rows)) })
    const { queryClient } = renderWithRouter('/envanter')
    const table = await screen.findByRole('table', { name: 'Demirbaş listesi' })
    await within(table).findByText('DMR-0001')
    queryClient.setQueryData(dashboardQueryKey, { totalCount: 1 })

    rows = [...rows, listItem({ id: 7, assetCode: 'DMR-0007' })]
    announce(event, 7)

    expect(await within(table).findByText('DMR-0007')).toBeInTheDocument()
    expect(gets(requests, '/api/assets').map((r) => r.headers['x-background-request'] ?? 'user')).toEqual(['user', '1'])
    expect(queryClient.getQueryState(dashboardQueryKey)?.isInvalidated).toBe(true)
  })

  it('leave the page of another asset alone', async () => {
    const requests = mockApi({ ...pageRoutes, 'GET /api/assets/1': json(200, details()) })
    renderWithRouter('/envanter/1')
    await screen.findByRole('heading', { level: 1, name: 'DMR-0001' })

    announce('AssetUpdated', 2)
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)))

    expect(gets(requests, '/api/assets/1')).toHaveLength(1)
    expect(gets(requests, '/api/assets/1/history')).toHaveLength(1)
  })

  it('say when changes no longer show up on their own', async () => {
    mockApi({ 'GET /api/assets': json(200, pageOf([])) })
    renderWithRouter('/envanter')
    await waitFor(() => expect(liveStatus()).toHaveTextContent('Canlı'))

    act(() => fakeHub.latest().close(new Error('Bağlantı koptu')))

    expect(liveStatus()).toHaveTextContent('Canlı güncelleme yok')
  })

  it('say so when the connection cannot be opened', async () => {
    fakeHub.configure = (connection) => {
      connection.startResult = () => Promise.reject(new Error('negotiate 503'))
    }
    mockApi({ 'GET /api/assets': json(200, pageOf([])) })
    renderWithRouter('/envanter')

    await waitFor(() => expect(liveStatus()).toHaveTextContent('Canlı güncelleme yok'))
  })

  it('close the connection when the app is left', async () => {
    mockApi({ 'GET /api/assets': json(200, pageOf([])) })
    const { unmount } = renderWithRouter('/envanter')
    await waitFor(() => expect(liveStatus()).toHaveTextContent('Canlı'))

    unmount()

    expect(fakeHub.latest().stopped).toBe(true)
  })

  it('warn while editing when someone else saved the asset, and load their version on request', async () => {
    let version = 1
    mockApi({
      ...lookupRoutes,
      'GET /api/assets/1': () => json(200, details({ computerName: `PC-V${version}`, rowVersion: `AAAAAAAAB9${version}=` })),
    })
    renderWithRouter('/envanter/1/duzenle')
    const computerName = await screen.findByRole('textbox', { name: 'Bilgisayar Adı' })
    await waitFor(() => expect(computerName).toHaveValue('PC-V1'))
    fireEvent.change(computerName, { target: { value: 'BENIM-DEGISIKLIGIM' } })

    version = 2
    announce('AssetUpdated', 1)

    const warning = await screen.findByText(/siz düzenlerken başka bir kullanıcı tarafından değiştirildi/)
    // The edit stays on the version it started from, with the user's own typing.
    expect(screen.getByRole('textbox', { name: 'Bilgisayar Adı' })).toHaveValue('BENIM-DEGISIKLIGIM')

    fireEvent.click(within(warning.closest('[role="alert"]') as HTMLElement).getByRole('button', { name: 'Güncel kaydı yükle' }))

    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Bilgisayar Adı' })).toHaveValue('PC-V2'))
    expect(screen.getByText(/Kaydın güncel hali yüklendi/)).toBeInTheDocument()
    expect(screen.queryByText(/siz düzenlerken başka bir kullanıcı/)).not.toBeInTheDocument()
  })
})
