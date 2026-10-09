import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { AssetDetails, AssetHistoryEntry } from '../inventory/assetsApi'
import type { AssetAssignmentItem, EmployeeSearchItem } from '../inventory/assignmentsApi'
import { changesOf } from '../inventory/historyChanges'
import { details } from '../test/assetData'
import { json, mockApi } from '../test/mockApi'
import { renderWithRouter } from '../test/renderWithRouter'

const csrf = { 'GET /api/auth/csrf': json(200, { token: 'csrf-token' }) }
const emptyHistory = json(200, { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 })

const ali: EmployeeSearchItem = {
  objectGuid: '6f1c2c58-1d1e-4a52-9b0e-6a3e8f4d2b10',
  userName: 'ali.kaya',
  displayName: 'Ali Kaya',
  email: 'ali.kaya@example.invalid',
  department: 'Muhasebe',
  title: 'Uzman',
}

const assignedTo = (holder: EmployeeSearchItem, overrides: Partial<AssetDetails> = {}): AssetDetails =>
  details({
    status: 'Assigned',
    rowVersion: 'AAAAAAAAB9I=',
    activeAssignment: {
      id: 5,
      employeeId: 9,
      userName: holder.userName,
      displayName: holder.displayName,
      assignmentDescription: 'Dizüstü + çanta',
      assignedAt: '2026-10-02T10:00:00Z',
      assignedBy: 'ayse.yilmaz',
    },
    ...overrides,
  })

const period = (overrides: Partial<AssetAssignmentItem> = {}): AssetAssignmentItem => ({
  id: 5,
  employeeId: 9,
  userName: 'ali.kaya',
  displayName: 'Ali Kaya',
  department: 'Muhasebe',
  assignmentDescription: 'Dizüstü + çanta',
  notes: null,
  assignedAt: '2026-10-02T10:00:00Z',
  assignedBy: 'ayse.yilmaz',
  returnedAt: null,
  returnedBy: null,
  ...overrides,
})

const pageOf = (items: AssetAssignmentItem[]) => json(200, { items, page: 1, pageSize: 10, totalCount: items.length, totalPages: items.length ? 1 : 0 })

function field(label: string) {
  return screen.getByText(label, { selector: 'dt' }).nextElementSibling
}

async function openAssignDialog() {
  await screen.findByRole('heading', { level: 1, name: 'DMR-0001' })
  fireEvent.click(screen.getByRole('button', { name: 'Zimmet Ver' }))
  return screen.findByRole('dialog', { name: 'Zimmet ver' })
}

/** Types into the employee box as a user does: the box has the focus while typing. */
function typeEmployee(dialog: HTMLElement, text: string) {
  const input = within(dialog).getByRole('combobox', { name: /Çalışan/ })
  input.focus()
  fireEvent.change(input, { target: { value: text } })
}

async function pickEmployee(dialog: HTMLElement, typed: string, option: RegExp) {
  typeEmployee(dialog, typed)
  fireEvent.click(await screen.findByRole('option', { name: option }))
}

describe('Assigning an asset', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('searches the directory, gives the asset to the chosen employee and shows who holds it', async () => {
    let current = details()
    let periods: AssetAssignmentItem[] = []
    const requests = mockApi({
      ...csrf,
      'GET /api/assets/1': () => json(200, current),
      'GET /api/assets/1/history': emptyHistory,
      'GET /api/assets/1/assignments': () => pageOf(periods),
      'GET /api/employees/search': json(200, { items: [ali], hasMore: false }),
      'POST /api/assets/1/assignments': () => {
        current = assignedTo(ali)
        periods = [period({ notes: 'Kutusuyla' })]
        return json(201, current)
      },
    })
    renderWithRouter('/envanter/1')
    expect(await screen.findByText('Henüz zimmet kaydı yok')).toBeInTheDocument()

    const dialog = await openAssignDialog()
    await pickEmployee(dialog, 'ali', /Ali Kaya/)
    expect(screen.queryByRole('option')).not.toBeInTheDocument()
    fireEvent.change(within(dialog).getByRole('textbox', { name: /Zimmet Tanımı/ }), { target: { value: '  Dizüstü + çanta ' } })
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Not' }), { target: { value: 'Kutusuyla' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Zimmet Ver' }))

    expect(await screen.findByText('DMR-0001, Ali Kaya adlı çalışana zimmetlendi.')).toBeInTheDocument()
    expect(field('Zimmetli Kişi')).toHaveTextContent('Ali Kaya (ali.kaya)')
    // The dialog fades out first; until then the page behind it is hidden from assistive technology.
    expect(await screen.findByRole('button', { name: 'İade Al' })).toBeInTheDocument()
    const history = await screen.findByRole('list', { name: 'Zimmet geçmişi' })
    expect(history).toHaveTextContent('Zimmette')
    expect(history).toHaveTextContent('Kutusuyla')

    const searches = requests.filter((r) => r.path.startsWith('/api/employees/search'))
    expect(searches.map((r) => r.path)).toEqual(['/api/employees/search?q=ali'])
    const post = requests.find((r) => r.method === 'POST')!
    expect(post.path).toBe('/api/assets/1/assignments')
    expect(post.headers['x-csrf-token']).toBe('csrf-token')
    expect(post.body).toEqual({
      employeeObjectGuid: ali.objectGuid,
      assignmentDescription: 'Dizüstü + çanta',
      notes: 'Kutusuyla',
      rowVersion: 'AAAAAAAAB9E=',
    })
  })

  it('asks for an employee and a description before sending anything', async () => {
    const requests = mockApi({
      'GET /api/assets/1': json(200, details()),
      'GET /api/assets/1/history': emptyHistory,
      'GET /api/assets/1/assignments': pageOf([]),
    })
    renderWithRouter('/envanter/1')
    const dialog = await openAssignDialog()

    fireEvent.click(within(dialog).getByRole('button', { name: 'Zimmet Ver' }))

    expect(await within(dialog).findByText('Zimmetlenecek çalışanı seçin.')).toBeInTheDocument()
    expect(within(dialog).getByText('Zimmet tanımı zorunludur.')).toBeInTheDocument()
    expect(requests.filter((r) => r.method === 'POST')).toEqual([])
  })

  it('does not search until two letters are typed and says when the directory cannot be reached', async () => {
    const requests = mockApi({
      'GET /api/assets/1': json(200, details()),
      'GET /api/assets/1/history': emptyHistory,
      'GET /api/assets/1/assignments': pageOf([]),
      'GET /api/employees/search': json(503, { title: 'Çalışan dizinine şu anda ulaşılamıyor.', code: 'directory_unavailable' }),
    })
    renderWithRouter('/envanter/1')
    const dialog = await openAssignDialog()

    typeEmployee(dialog, 'a')
    expect(await screen.findByText('Aramak için en az 2 harf yazın.')).toBeInTheDocument()
    typeEmployee(dialog, 'al')

    expect(await screen.findByText('Çalışan dizinine şu anda ulaşılamıyor. Biraz sonra tekrar deneyin.')).toBeInTheDocument()
    expect(requests.filter((r) => r.path.startsWith('/api/employees')).map((r) => r.path)).toEqual(['/api/employees/search?q=al'])
  })

  it('keeps the dialog open for another choice when the employee account is disabled', async () => {
    mockApi({
      ...csrf,
      'GET /api/assets/1': json(200, details()),
      'GET /api/assets/1/history': emptyHistory,
      'GET /api/assets/1/assignments': pageOf([]),
      'GET /api/employees/search': json(200, { items: [ali], hasMore: true }),
      'POST /api/assets/1/assignments': json(409, {
        title: "Çalışanın Active Directory hesabı pasif.",
        detail: 'Pasif hesaplı çalışana demirbaş zimmetlenemez.',
        code: 'employee_inactive',
      }),
    })
    renderWithRouter('/envanter/1')
    const dialog = await openAssignDialog()
    typeEmployee(dialog, 'ali')
    expect(await within(dialog).findByText(/İlk sonuçlar gösteriliyor/)).toBeInTheDocument()
    fireEvent.click(await screen.findByRole('option', { name: /Ali Kaya/ }))
    fireEvent.change(within(dialog).getByRole('textbox', { name: /Zimmet Tanımı/ }), { target: { value: 'Dizüstü' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Zimmet Ver' }))

    expect(await within(dialog).findByText("Çalışanın Active Directory hesabı pasif.")).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Zimmet Ver' })).toBeEnabled()
  })

  it('does not assign an asset someone changed meanwhile and shows it as it is now', async () => {
    let version = 1
    mockApi({
      ...csrf,
      'GET /api/assets/1': () => json(200, details({ rowVersion: `v${version}`, computerName: `PC-V${version}` })),
      'GET /api/assets/1/history': emptyHistory,
      'GET /api/assets/1/assignments': pageOf([]),
      'GET /api/employees/search': json(200, { items: [ali], hasMore: false }),
      'POST /api/assets/1/assignments': json(409, { title: 'Kayıt değiştirildi.', code: 'concurrency_conflict' }),
    })
    renderWithRouter('/envanter/1')
    const dialog = await openAssignDialog()
    version = 2
    await pickEmployee(dialog, 'ali', /Ali Kaya/)
    fireEvent.change(within(dialog).getByRole('textbox', { name: /Zimmet Tanımı/ }), { target: { value: 'Dizüstü' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Zimmet Ver' }))

    expect(await within(dialog).findByText('Kayıt siz bakarken değiştirildi.')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Zimmet Ver' })).toBeDisabled()
    await waitFor(() => expect(field('Bilgisayar Adı')).toHaveTextContent('PC-V2'))
  })

  it('does not offer assigning a faulty asset', async () => {
    mockApi({
      'GET /api/assets/1': json(200, details({ status: 'Faulty' })),
      'GET /api/assets/1/history': emptyHistory,
      'GET /api/assets/1/assignments': pageOf([]),
    })
    renderWithRouter('/envanter/1')

    await screen.findByRole('heading', { level: 1, name: 'DMR-0001' })
    expect(screen.getByRole('button', { name: 'Zimmet Ver' })).toBeDisabled()
  })
})

describe('Taking an asset back', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('returns the asset after confirmation and keeps the period in the history', async () => {
    let current = assignedTo(ali)
    let periods = [period()]
    const requests = mockApi({
      ...csrf,
      'GET /api/assets/1': () => json(200, current),
      'GET /api/assets/1/history': emptyHistory,
      'GET /api/assets/1/assignments': () => pageOf(periods),
      'POST /api/assets/1/returns': () => {
        current = details({ rowVersion: 'AAAAAAAAB9M=' })
        periods = [period({ returnedAt: '2026-10-06T15:00:00Z', returnedBy: 'ayse.yilmaz' })]
        return json(200, current)
      },
    })
    renderWithRouter('/envanter/1')
    await screen.findByRole('heading', { level: 1, name: 'DMR-0001' })
    expect(screen.queryByRole('button', { name: 'Zimmet Ver' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'İade Al' }))
    const dialog = await screen.findByRole('dialog', { name: 'İade al' })
    expect(dialog).toHaveTextContent('DMR-0001, Ali Kaya (ali.kaya) adlı çalışandan iade alınacak')
    fireEvent.click(within(dialog).getByRole('button', { name: 'İade Al' }))

    expect(await screen.findByText('DMR-0001 iade alındı.')).toBeInTheDocument()
    expect(screen.getByText('Bu demirbaş kimseye zimmetli değil.')).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Zimmet Ver' })).toBeEnabled()
    const history = screen.getByRole('list', { name: 'Zimmet geçmişi' })
    await waitFor(() => expect(history).toHaveTextContent('İade alındı'))
    expect(history).toHaveTextContent('Ali Kaya (ali.kaya)')
    expect(requests.find((r) => r.method === 'POST')?.body).toEqual({ rowVersion: 'AAAAAAAAB9I=' })
  })

  it('does not return an asset someone changed meanwhile', async () => {
    mockApi({
      ...csrf,
      'GET /api/assets/1': json(200, assignedTo(ali)),
      'GET /api/assets/1/history': emptyHistory,
      'GET /api/assets/1/assignments': pageOf([period()]),
      'POST /api/assets/1/returns': json(409, { title: 'Kayıt değiştirildi.', code: 'concurrency_conflict' }),
    })
    renderWithRouter('/envanter/1')
    await screen.findByRole('heading', { level: 1, name: 'DMR-0001' })

    fireEvent.click(screen.getByRole('button', { name: 'İade Al' }))
    const dialog = await screen.findByRole('dialog', { name: 'İade al' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'İade Al' }))

    expect(await within(dialog).findByText('Kayıt siz bakarken değiştirildi.')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'İade Al' })).toBeDisabled()
  })
})

describe('Assignment entries of the asset history', () => {
  const entry = (action: string, oldValues: Record<string, unknown>, newValues: Record<string, unknown>): AssetHistoryEntry => ({
    id: 1,
    action,
    userName: 'ayse.yilmaz',
    timestamp: '2026-10-02T10:00:00Z',
    correlationId: 'c-1',
    oldValues,
    newValues,
  })

  it('names who got the asset and who gave it back, without internal identifiers', () => {
    const assigned = changesOf(
      entry(
        'Assigned',
        { status: 'Available' },
        {
          status: 'Assigned',
          assignmentId: 5,
          employeeId: 9,
          employeeObjectGuid: ali.objectGuid,
          employeeUserName: 'ali.kaya',
          employeeDisplayName: 'Ali Kaya',
          assignmentDescription: 'Dizüstü',
          notes: null,
          assignedAt: '2026-10-02T10:00:00Z',
        },
      ),
    ).map((change) => `${change.field}: ${change.text}`)
    expect(assigned).toEqual([
      'Durum: Boşta → Zimmetli',
      'Çalışan: Ali Kaya',
      'Kullanıcı adı: ali.kaya',
      'Zimmet tanımı: Dizüstü',
      expect.stringMatching(/^Zimmet tarihi: \d{1,2}\.\d{1,2}\.\d{4} \d{2}:\d{2}$/),
    ])

    const returned = changesOf(
      entry(
        'Returned',
        { status: 'Assigned', assignmentId: 5, employeeDisplayName: 'Ali Kaya', employeeUserName: 'ali.kaya', assignedAt: '2026-10-02T10:00:00Z' },
        { status: 'Available', returnedAt: '2026-10-06T15:00:00Z' },
      ),
    ).map((change) => `${change.field}: ${change.text}`)
    expect(returned.slice(0, 3)).toEqual(['Durum: Zimmetli → Boşta', 'Çalışan: Ali Kaya', 'Kullanıcı adı: ali.kaya'])
    expect(returned.at(-1)).toMatch(/^İade tarihi: /)
  })
})
