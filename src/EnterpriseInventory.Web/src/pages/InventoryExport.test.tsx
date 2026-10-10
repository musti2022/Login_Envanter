import { fireEvent, screen, waitFor } from '@testing-library/react'
import { listItem, pageOf } from '../test/assetData'
import { json, mockApi } from '../test/mockApi'
import { renderWithRouter } from '../test/renderWithRouter'

const xlsxType = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'

function excelFile() {
  return new Response('PK-xlsx', {
    status: 200,
    headers: {
      'Content-Type': xlsxType,
      'Content-Disposition': "attachment; filename=envanter-2026-10-10.xlsx; filename*=UTF-8''envanter-2026-10-10.xlsx",
    },
  })
}

/** What the browser was asked to save: the anchor's name and the blob behind its address. */
function captureSaves() {
  const saved: { fileName: string; blob: Blob }[] = []
  const blobs = new Map<string, Blob>()
  URL.createObjectURL = vi.fn((blob: Blob) => {
    const url = `blob:test/${blobs.size + 1}`
    blobs.set(url, blob)
    return url
  })
  URL.revokeObjectURL = vi.fn()
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    saved.push({ fileName: this.download, blob: blobs.get(this.getAttribute('href') ?? '')! })
  })
  return saved
}

function exportButton() {
  return screen.getByRole('button', { name: "Excel'e aktar" })
}

// jsdom has no object URLs; captureSaves puts fakes in their place.
const { createObjectURL, revokeObjectURL } = URL

describe('Excel export', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    URL.createObjectURL = createObjectURL
    URL.revokeObjectURL = revokeObjectURL
  })

  it('downloads every page of the list on screen, with its filters and order', async () => {
    const saved = captureSaves()
    const requests = mockApi({
      'GET /api/assets': json(200, pageOf([listItem()], { pageSize: 10, totalCount: 40 })),
      'GET /api/assets/export': excelFile(),
    })
    renderWithRouter('/envanter?search=pc&status=Faulty&sortBy=cityName&sortDirection=desc&page=2&pageSize=10')
    await screen.findByRole('table', { name: 'Demirbaş listesi' })

    fireEvent.click(exportButton())

    await waitFor(() => expect(saved).toHaveLength(1))
    // The same filters and order as the list; the page on screen does not narrow the file.
    expect(requests.filter((r) => r.path.startsWith('/api/assets/export')).map((r) => r.path)).toEqual([
      '/api/assets/export?search=pc&status=Faulty&sortBy=cityName&sortDirection=desc',
    ])
    expect(saved[0].fileName).toBe('envanter-2026-10-10.xlsx')
    expect(await saved[0].blob.text()).toBe('PK-xlsx')
    await waitFor(() => expect(exportButton()).toBeEnabled())
  })

  it('exports the archive when the archive is on screen', async () => {
    captureSaves()
    const requests = mockApi({
      'GET /api/assets': json(200, pageOf([listItem({ isArchived: true })])),
      'GET /api/assets/export': excelFile(),
    })
    renderWithRouter('/envanter?archived=true')
    await screen.findByRole('table', { name: 'Demirbaş listesi' })

    fireEvent.click(exportButton())

    await waitFor(() => expect(requests.map((r) => r.path)).toContain('/api/assets/export?archived=true'))
  })

  it('shows that the file is being prepared and cannot be asked for twice meanwhile', async () => {
    const saved = captureSaves()
    let finish: (response: Response) => void = () => {}
    mockApi({
      'GET /api/assets': json(200, pageOf([listItem()])),
      'GET /api/assets/export': () => new Promise<Response>((resolve) => (finish = resolve)),
    })
    renderWithRouter('/envanter')
    await screen.findByRole('table', { name: 'Demirbaş listesi' })

    fireEvent.click(exportButton())

    const pending = await screen.findByRole('button', { name: 'Hazırlanıyor...' })
    expect(pending).toBeDisabled()
    finish(excelFile())
    await waitFor(() => expect(saved).toHaveLength(1))
    await waitFor(() => expect(exportButton()).toBeEnabled())
  })

  it('says why a list too long for one file was refused', async () => {
    const saved = captureSaves()
    mockApi({
      'GET /api/assets': json(200, pageOf([listItem()], { totalCount: 60_000 })),
      'GET /api/assets/export': json(400, {
        title: 'Aktarılacak demirbaş sayısı sınırı aşıyor.',
        detail:
          'Filtrelerle eşleşen 60.000 demirbaş var; bir dosyaya en fazla 50.000 demirbaş aktarılabilir. Filtreleri daraltıp tekrar deneyin.',
        status: 400,
        code: 'export_too_large',
      }),
    })
    renderWithRouter('/envanter')
    await screen.findByRole('table', { name: 'Demirbaş listesi' })

    fireEvent.click(exportButton())

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Aktarılacak demirbaş sayısı sınırı aşıyor.')
    expect(alert).toHaveTextContent('en fazla 50.000 demirbaş aktarılabilir')
    expect(saved).toHaveLength(0)
    expect(exportButton()).toBeEnabled()

    fireEvent.click(screen.getByRole('button', { name: /close|kapat/i }))
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
  })

  it('says so when the server cannot be reached', async () => {
    captureSaves()
    mockApi({
      'GET /api/assets': json(200, pageOf([listItem()])),
      'GET /api/assets/export': () => {
        throw new TypeError('Failed to fetch')
      },
    })
    renderWithRouter('/envanter')
    await screen.findByRole('table', { name: 'Demirbaş listesi' })

    fireEvent.click(exportButton())

    expect(await screen.findByRole('alert')).toHaveTextContent('Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.')
  })

  it('is off when nothing is listed', async () => {
    mockApi({ 'GET /api/assets': json(200, pageOf([], { totalCount: 0 })) })
    renderWithRouter('/envanter?status=Retired')

    await screen.findByText('Filtrelerle eşleşen demirbaş yok')
    expect(exportButton()).toBeDisabled()
  })
})
