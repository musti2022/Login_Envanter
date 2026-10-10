import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import type { AssetDetails } from '../inventory/assetsApi'
import { details } from '../test/assetData'
import { lookupRoutes } from '../test/lookupData'
import { json, mockApi } from '../test/mockApi'
import { renderWithRouter } from '../test/renderWithRouter'

const csrf = { 'GET /api/auth/csrf': json(200, { token: 'csrf-token' }) }
const emptyPage = json(200, { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 })
const pageRoutes = { 'GET /api/assets/1/history': emptyPage, 'GET /api/assets/1/assignments': emptyPage }

function field(label: string) {
  return screen.getByText(label, { selector: 'dt' }).nextElementSibling
}

/**
 * Picks an option of a select in the dialog. The shared helper closes a select with Escape, which would close
 * the dialog too; a single select closes by itself on the click.
 */
async function choose(dialog: HTMLElement, select: RegExp, option: string) {
  fireEvent.mouseDown(within(dialog).getByRole('combobox', { name: select }))
  fireEvent.click(within(await screen.findByRole('listbox')).getByRole('option', { name: option }))
  await waitFor(() => expect(screen.queryByRole('listbox')).not.toBeInTheDocument())
}

async function optionsOf(dialog: HTMLElement, select: RegExp) {
  fireEvent.mouseDown(within(dialog).getByRole('combobox', { name: select }))
  const listbox = await screen.findByRole('listbox')
  const options = within(listbox).getAllByRole('option').map((option) => option.textContent)
  fireEvent.click(within(listbox).getAllByRole('option')[0])
  await waitFor(() => expect(screen.queryByRole('listbox')).not.toBeInTheDocument())
  return options
}

async function openMoveDialog() {
  await screen.findByRole('heading', { level: 1, name: 'DMR-0001' })
  fireEvent.click(screen.getByRole('button', { name: 'Konum Değiştir' }))
  return screen.findByRole('dialog', { name: 'Konum değiştir' })
}

describe('Moving an asset', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('starts from where the asset is, sends only the new place and shows it', async () => {
    let current: AssetDetails = details()
    const requests = mockApi({
      ...csrf,
      ...lookupRoutes,
      ...pageRoutes,
      'GET /api/assets/1': () => json(200, current),
      'PUT /api/assets/1/location': () => {
        current = details({ city: { id: 5, name: 'Ankara' }, location: null, department: { id: 9, name: 'Muhasebe' }, rowVersion: 'AAAAAAAAB9M=' })
        return json(200, current)
      },
    })
    renderWithRouter('/envanter/1')
    const dialog = await openMoveDialog()

    await waitFor(() => expect(within(dialog).getByRole('combobox', { name: /Şehir/ })).toHaveTextContent('İstanbul'))
    expect(within(dialog).getByRole('combobox', { name: /Lokasyon/ })).toHaveTextContent('Merkez Ofis')
    expect(within(dialog).getByRole('combobox', { name: /Departman/ })).toHaveTextContent('Bilgi İşlem')
    expect(within(dialog).getByRole('button', { name: 'Konumu Kaydet' })).toBeDisabled()
    // An inactive location is not a new choice.
    expect(await optionsOf(dialog, /Lokasyon/)).toEqual(['Seçilmedi', 'Merkez Ofis'])

    await choose(dialog, /Şehir/, 'Ankara')
    expect(within(dialog).getByRole('combobox', { name: /Lokasyon/ })).not.toHaveTextContent('Merkez Ofis')
    await choose(dialog, /Departman/, 'Muhasebe')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Konumu Kaydet' }))

    expect(await screen.findByText('DMR-0001 konumu değiştirildi.')).toBeInTheDocument()
    expect(field('Şehir')).toHaveTextContent('Ankara')
    expect(field('Departman')).toHaveTextContent('Muhasebe')
    const put = requests.find((r) => r.method === 'PUT')!
    expect(put.path).toBe('/api/assets/1/location')
    expect(put.headers['x-csrf-token']).toBe('csrf-token')
    expect(put.body).toEqual({ cityId: 5, departmentId: 9, locationId: null, rowVersion: 'AAAAAAAAB9E=' })
  })

  it('shows the API refusal under the field it is about', async () => {
    mockApi({
      ...csrf,
      ...lookupRoutes,
      ...pageRoutes,
      'GET /api/assets/1': json(200, details()),
      'PUT /api/assets/1/location': json(400, {
        title: 'Girilen bilgiler geçersiz.',
        errors: { departmentId: ['Seçilen departman pasif; yeni seçimlerde kullanılamaz.'] },
      }),
    })
    renderWithRouter('/envanter/1')
    const dialog = await openMoveDialog()
    await waitFor(() => expect(within(dialog).getByRole('combobox', { name: /Departman/ })).toHaveTextContent('Bilgi İşlem'))

    await choose(dialog, /Departman/, 'Muhasebe')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Konumu Kaydet' }))

    expect(await within(dialog).findByText('Seçilen departman pasif; yeni seçimlerde kullanılamaz.')).toBeInTheDocument()
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
  })

  it('does not move an asset someone changed meanwhile and shows it as it is now', async () => {
    let version = 1
    mockApi({
      ...csrf,
      ...lookupRoutes,
      ...pageRoutes,
      'GET /api/assets/1': () => json(200, details({ rowVersion: `v${version}`, computerName: `PC-V${version}` })),
      'PUT /api/assets/1/location': json(409, { title: 'Kayıt değiştirildi.', code: 'concurrency_conflict' }),
    })
    renderWithRouter('/envanter/1')
    const dialog = await openMoveDialog()
    version = 2
    await waitFor(() => expect(within(dialog).getByRole('combobox', { name: /Departman/ })).toHaveTextContent('Bilgi İşlem'))

    await choose(dialog, /Departman/, 'Muhasebe')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Konumu Kaydet' }))

    expect(await within(dialog).findByText('Kayıt siz bakarken değiştirildi.')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Konumu Kaydet' })).toBeDisabled()
    await waitFor(() => expect(field('Bilgisayar Adı')).toHaveTextContent('PC-V2'))
  })

  it('keeps a value the asset has although it was deactivated since', async () => {
    mockApi({
      ...lookupRoutes,
      ...pageRoutes,
      'GET /api/assets/1': json(200, details({ location: { id: 61, name: 'Depo' } })),
    })
    renderWithRouter('/envanter/1')
    const dialog = await openMoveDialog()

    await waitFor(() => expect(within(dialog).getByRole('combobox', { name: /Lokasyon/ })).toHaveTextContent('Depo (pasif)'))
  })

  it('is not offered for an archived asset', async () => {
    mockApi({ ...lookupRoutes, ...pageRoutes, 'GET /api/assets/1': json(200, details({ isArchived: true })) })
    renderWithRouter('/envanter/1')

    await screen.findByRole('heading', { level: 1, name: 'DMR-0001' })
    expect(screen.queryByRole('button', { name: 'Konum Değiştir' })).not.toBeInTheDocument()
  })
})
