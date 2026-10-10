import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, fireEvent, renderHook, screen, waitFor, within } from '@testing-library/react'
import { auditLogsQueryKey } from '../audit/auditApi'
import { dashboardQueryKey } from '../dashboard/dashboardApi'
import { assetsQueryKey } from '../inventory/assetsApi'
import { details, listItem, pageOf } from '../test/assetData'
import { fakeHub } from '../test/fakeHub'
import { lookupRoutes } from '../test/lookupData'
import { json, mockApi, type RecordedRequest } from '../test/mockApi'
import { renderWithRouter } from '../test/renderWithRouter'
import { reconnectDelay } from './reconnect'
import { useLiveUpdates } from './useLiveUpdates'

// Tries again at once, so a test plays an outage by what each start() does; reconnect.test.ts checks the delays.
vi.mock('./reconnect', () => ({ reconnectDelay: vi.fn(() => 0) }))

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

/** start() answers in turn with each of these, then leaves the try pending. */
function startsWith(...results: (() => Promise<void>)[]) {
  fakeHub.configure = (connection) => {
    let tries = 0
    connection.startResult = () => (results[tries++] ?? (() => new Promise<void>(() => {})))()
  }
}

const connects = () => Promise.resolve()
const fails = () => Promise.reject(new Error('negotiate 503'))

/** The hub announces a change, as SignalR would deliver it. */
function announce(event: string, assetId: number) {
  act(() => fakeHub.latest().emit(event, { assetId, occurredAt: '2026-10-09T12:00:00Z' }))
}

describe('Live updates', () => {
  beforeEach(() => vi.mocked(reconnectDelay).mockClear())
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
  ])('refresh the inventory list, the dashboard and the audit log on %s of any asset', async (event) => {
    let rows = [listItem({ id: 1, assetCode: 'DMR-0001' })]
    const requests = mockApi({ 'GET /api/assets': () => json(200, pageOf(rows)) })
    const { queryClient } = renderWithRouter('/envanter')
    const table = await screen.findByRole('table', { name: 'Demirbaş listesi' })
    await within(table).findByText('DMR-0001')
    queryClient.setQueryData(dashboardQueryKey, { totalCount: 1 })
    const auditKey = [...auditLogsQueryKey, 'list', { page: 1 }]
    queryClient.setQueryData(auditKey, { items: [] })

    rows = [...rows, listItem({ id: 7, assetCode: 'DMR-0007' })]
    announce(event, 7)

    expect(await within(table).findByText('DMR-0007')).toBeInTheDocument()
    expect(gets(requests, '/api/assets').map((r) => r.headers['x-background-request'] ?? 'user')).toEqual(['user', '1'])
    expect(queryClient.getQueryState(dashboardQueryKey)?.isInvalidated).toBe(true)
    expect(queryClient.getQueryState(auditKey)?.isInvalidated).toBe(true)
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

  it('say so while a lost connection is tried again', async () => {
    startsWith(connects)
    mockApi({ 'GET /api/assets': json(200, pageOf([])) })
    renderWithRouter('/envanter')
    await waitFor(() => expect(liveStatus()).toHaveTextContent('Canlı'))

    act(() => fakeHub.latest().close(new Error('Bağlantı koptu')))

    expect(liveStatus()).toHaveTextContent('Yeniden bağlanıyor')
    await waitFor(() => expect(fakeHub.latest().starts).toBe(2))
    // A lost connection is tried again at once.
    expect(vi.mocked(reconnectDelay).mock.calls).toEqual([[0]])
  })

  it('keep trying a connection that cannot be opened, backing off after each failure', async () => {
    startsWith(fails, fails, fails)
    mockApi({ 'GET /api/assets': json(200, pageOf([])) })
    renderWithRouter('/envanter')

    await waitFor(() => expect(fakeHub.latest().starts).toBe(4))
    expect(liveStatus()).toHaveTextContent('Yeniden bağlanıyor')
    expect(vi.mocked(reconnectDelay).mock.calls).toEqual([[1], [2], [3]])
    expect(fakeHub.connections).toHaveLength(1)
  })

  it('fetch everything shown again once a lost connection is back, as background reads', async () => {
    let version = 1
    startsWith(connects, fails, connects)
    const requests = mockApi({
      ...pageRoutes,
      'GET /api/assets/1': () => json(200, details({ computerName: `PC-V${version}`, rowVersion: `AAAAAAAAB9${version}=` })),
    })
    const { queryClient } = renderWithRouter('/envanter/1')
    await waitFor(() => expect(field('Bilgisayar Adı')).toHaveTextContent('PC-V1'))
    await waitFor(() => expect(liveStatus()).toHaveTextContent('Canlı'))
    queryClient.setQueryData(dashboardQueryKey, { totalCount: 1 })

    // Someone else saves while this screen is not connected: no notification reaches it.
    act(() => fakeHub.latest().close(new Error('Bağlantı koptu')))
    version = 2

    await waitFor(() => expect(field('Bilgisayar Adı')).toHaveTextContent('PC-V2'))
    expect(liveStatus()).toHaveTextContent('Canlı')
    expect(gets(requests, '/api/assets/1').map((r) => r.headers['x-background-request'] ?? 'user')).toEqual(['user', '1'])
    expect(gets(requests, '/api/assets/1/history')).toHaveLength(2)
    expect(gets(requests, '/api/assets/1/assignments')).toHaveLength(2)
    // What is not shown is fetched when next shown; the session itself is not asked again.
    expect(queryClient.getQueryState(dashboardQueryKey)?.isInvalidated).toBe(true)
    expect(gets(requests, '/api/auth/me')).toHaveLength(0)
  })

  it('fetch everything again when the connection opens only after failed tries', async () => {
    startsWith(fails, connects)
    const requests = mockApi({ 'GET /api/assets': json(200, pageOf([])) })
    renderWithRouter('/envanter')

    await waitFor(() => expect(liveStatus()).toHaveTextContent('Canlı'))
    await waitFor(() => expect(gets(requests, '/api/assets').map((r) => r.headers['x-background-request'] ?? 'user')).toEqual(['user', '1']))
  })

  it('not fetch again when the first try connects', async () => {
    let connect!: () => void
    startsWith(() => new Promise<void>((resolve) => (connect = resolve)))
    const requests = mockApi({ 'GET /api/assets': json(200, pageOf([])) })
    renderWithRouter('/envanter')
    await waitFor(() => expect(gets(requests, '/api/assets')).toHaveLength(1))

    act(() => connect())
    await waitFor(() => expect(liveStatus()).toHaveTextContent('Canlı'))
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)))

    expect(gets(requests, '/api/assets')).toHaveLength(1)
  })

  it('send the user to sign in when the connection is refused because the session has ended', async () => {
    startsWith(connects, () => fakeHub.latest().refuseEndedSession())
    mockApi({ 'GET /api/assets': json(200, pageOf([])) })
    const { router, queryClient } = renderWithRouter('/envanter')
    await waitFor(() => expect(liveStatus()).toHaveTextContent('Canlı'))

    act(() => fakeHub.latest().close(new Error('Oturum kapatıldı')))

    await waitFor(() => expect(router.state.location.pathname).toBe('/giris'))
    expect(await screen.findByText('Oturumunuz sona erdi. Devam etmek için tekrar giriş yapın.')).toBeInTheDocument()
    expect(queryClient.getQueryCache().findAll({ queryKey: assetsQueryKey })).toHaveLength(0)
    const connection = fakeHub.latest()
    expect(connection.stopped).toBe(true)
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)))
    expect(connection.starts).toBe(2)
  })

  it('stop trying once the session has ended, and say so', async () => {
    startsWith(connects, () => fakeHub.latest().refuseEndedSession())
    const { result } = renderHook(() => useLiveUpdates(), {
      wrapper: ({ children }) => <QueryClientProvider client={new QueryClient()}>{children}</QueryClientProvider>,
    })
    await waitFor(() => expect(result.current).toBe('connected'))

    act(() => fakeHub.latest().close(new Error('Oturum kapatıldı')))

    await waitFor(() => expect(result.current).toBe('disconnected'))
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)))
    expect(result.current).toBe('disconnected')
    expect(fakeHub.latest().starts).toBe(2)
  })

  it('stop trying when the app is left', async () => {
    let failNow!: () => void
    startsWith(fails, () => new Promise<void>((_, reject) => (failNow = () => reject(new Error('negotiate 503')))))
    mockApi({ 'GET /api/assets': json(200, pageOf([])) })
    const { unmount } = renderWithRouter('/envanter')
    await waitFor(() => expect(fakeHub.latest().starts).toBe(2))

    unmount()
    failNow()
    await new Promise((resolve) => setTimeout(resolve, 50))

    expect(fakeHub.latest().stopped).toBe(true)
    expect(fakeHub.latest().starts).toBe(2)
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
