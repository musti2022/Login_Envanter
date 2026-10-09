import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { listItem, pageOf } from '../test/assetData'
import { lookupRoutes } from '../test/lookupData'
import { json, mockApi, type RecordedRequest } from '../test/mockApi'
import { renderWithRouter } from '../test/renderWithRouter'
import { choose } from '../test/select'
import { mockViewport } from '../test/viewport'

function listRequests(requests: RecordedRequest[]) {
  return requests.filter((r) => r.path.startsWith('/api/assets')).map((r) => r.path)
}

function lastListRequest(requests: RecordedRequest[]) {
  return listRequests(requests).at(-1)
}

function combobox(name: string) {
  return screen.getByRole('combobox', { name })
}

function setup(path: string, assetsPage = pageOf([listItem()])) {
  const requests = mockApi({ ...lookupRoutes, 'GET /api/assets': json(200, assetsPage) })
  const view = renderWithRouter(path)
  return { requests, ...view }
}

describe('inventory search and filters', () => {
  let viewport: ReturnType<typeof mockViewport>

  beforeEach(() => {
    viewport = mockViewport(true)
  })

  afterEach(() => {
    viewport.restore()
    vi.unstubAllGlobals()
  })

  it('searches once typing stops, from the first page', async () => {
    const { requests, router } = setup('/envanter?page=3&sortBy=cityName')
    await screen.findByRole('table', { name: 'Demirbaş listesi' })

    const search = screen.getByRole('searchbox', { name: 'Ara' })
    for (const text of ['d', 'de', 'del', 'dell ', 'dell iz']) {
      fireEvent.change(search, { target: { value: text } })
    }

    await waitFor(() => expect(lastListRequest(requests)).toBe('/api/assets?search=dell+iz&sortBy=cityName'))
    expect(listRequests(requests)).toEqual(['/api/assets?sortBy=cityName&page=3', '/api/assets?search=dell+iz&sortBy=cityName'])
    expect(router.state.location.search).toBe('?search=dell+iz&sortBy=cityName')
  })

  it('searches at once on Enter and shows the search of the address', async () => {
    const { requests } = setup('/envanter?search=pc-ist')
    await screen.findByRole('table', { name: 'Demirbaş listesi' })
    const search = screen.getByRole('searchbox', { name: 'Ara' })
    expect(search).toHaveValue('pc-ist')

    fireEvent.change(search, { target: { value: 'SN-100' } })
    fireEvent.keyDown(search, { key: 'Enter' })

    await waitFor(() => expect(lastListRequest(requests)).toBe('/api/assets?search=SN-100'))
  })

  it('filters by statuses and types, in the order of the list', async () => {
    const { requests, router } = setup('/envanter?page=2')
    await screen.findByRole('table', { name: 'Demirbaş listesi' })

    await choose('Durum', 'Hurda', 'Arızalı')
    await waitFor(() => expect(lastListRequest(requests)).toBe('/api/assets?status=Faulty&status=Retired'))
    expect(combobox('Durum')).toHaveTextContent('Arızalı, Hurda')

    await choose('Tür', 'Monitör')
    await waitFor(() => expect(lastListRequest(requests)).toBe('/api/assets?status=Faulty&status=Retired&assetType=Monitor'))
    expect(router.state.location.search).toBe('?status=Faulty&status=Retired&assetType=Monitor')
  })

  it('chooses a model within the chosen brand and forgets it when the brand changes', async () => {
    const { requests } = setup('/envanter')
    await screen.findByRole('table', { name: 'Demirbaş listesi' })
    expect(combobox('Model')).toHaveAttribute('aria-disabled', 'true')
    expect(screen.getByText('Önce marka seçin')).toBeInTheDocument()

    await choose('Marka', 'Dell')
    await waitFor(() => expect(lastListRequest(requests)).toBe('/api/assets?brandId=1'))
    await waitFor(() => expect(combobox('Model')).not.toHaveAttribute('aria-disabled'))
    await choose('Model', 'Optiplex 7010')
    await waitFor(() => expect(lastListRequest(requests)).toBe('/api/assets?brandId=1&modelId=12'))

    await choose('Marka', 'HP')
    await waitFor(() => expect(lastListRequest(requests)).toBe('/api/assets?brandId=3'))
    expect(listRequests(requests)).not.toContain('/api/assets?brandId=3&modelId=12')
    fireEvent.mouseDown(combobox('Model'))
    const options = within(await screen.findByRole('listbox')).getAllByRole('option').map((o) => o.textContent)
    expect(options).toEqual(['Tümü', 'EliteBook 840'])
    expect(requests.map((r) => r.path)).toContain('/api/models?brandId=3')
  })

  it('filters by city, location, department and archived assets', async () => {
    const { requests } = setup('/envanter')
    await screen.findByRole('table', { name: 'Demirbaş listesi' })

    await choose('Şehir', 'İstanbul')
    await waitFor(() => expect(combobox('Lokasyon')).not.toHaveAttribute('aria-disabled'))
    fireEvent.mouseDown(combobox('Lokasyon'))
    expect(within(await screen.findByRole('listbox')).getByRole('option', { name: 'Depo (pasif)' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('option', { name: 'Merkez Ofis' }))
    await choose('Departman', 'Muhasebe')
    fireEvent.click(screen.getByRole('switch', { name: 'Arşivlenmişleri göster' }))

    await waitFor(() => expect(lastListRequest(requests)).toBe('/api/assets?cityId=6&departmentId=9&locationId=60&archived=true'))
  })

  it('drops a model that does not belong to the brand of a link, without a new history entry', async () => {
    const { requests, router } = setup('/envanter?brandId=1&modelId=31')

    await waitFor(() => expect(router.state.location.search).toBe('?brandId=1'))
    expect(router.state.historyAction).toBe('REPLACE')
    await waitFor(() => expect(lastListRequest(requests)).toBe('/api/assets?brandId=1'))
    await waitFor(() => expect(combobox('Marka')).toHaveTextContent('Dell'))
  })

  it('says when no asset matches and clears every filter but the sorting', async () => {
    const { requests, router } = setup('/envanter?search=yok&status=Faulty&cityId=6&sortBy=cityName', pageOf([]))

    expect(await screen.findByText('Filtrelerle eşleşen demirbaş yok')).toBeInTheDocument()
    expect(screen.queryByText('Henüz demirbaş yok')).not.toBeInTheDocument()
    const [, emptyStateButton] = screen.getAllByRole('button', { name: 'Filtreleri temizle' })
    fireEvent.click(emptyStateButton)

    await waitFor(() => expect(router.state.location.search).toBe('?sortBy=cityName'))
    expect(lastListRequest(requests)).toBe('/api/assets?sortBy=cityName')
    expect(screen.getByRole('searchbox', { name: 'Ara' })).toHaveValue('')
    expect(screen.getByRole('button', { name: 'Filtreleri temizle' })).toBeDisabled()
  })

  it('marks a lookup list that could not be loaded', async () => {
    mockApi({ ...lookupRoutes, 'GET /api/brands': json(500, { title: 'Hata' }), 'GET /api/assets': json(200, pageOf([listItem()])) })
    renderWithRouter('/envanter')

    expect(await screen.findByText('Liste alınamadı.', {}, { timeout: 4000 })).toBeInTheDocument()
    expect(combobox('Şehir')).not.toHaveAttribute('aria-invalid', 'true')
  })

  it('folds the filters under a button on small screens', async () => {
    viewport.setDesktop(false)
    setup('/envanter?status=Faulty&cityId=6')
    await screen.findByRole('table', { name: 'Demirbaş listesi' })

    const toggle = screen.getByRole('button', { name: 'Filtreler, 2 filtre açık' })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('combobox', { name: 'Durum' })).not.toBeInTheDocument()
    expect(screen.getByRole('searchbox', { name: 'Ara' })).toBeVisible()

    fireEvent.click(toggle)

    expect(toggle).toHaveAttribute('aria-expanded', 'true')
    expect(await screen.findByRole('combobox', { name: 'Durum' })).toHaveTextContent('Arızalı')
  })
})
