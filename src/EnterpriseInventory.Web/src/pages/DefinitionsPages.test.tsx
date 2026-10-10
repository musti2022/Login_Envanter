import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { pageOf } from '../test/assetData'
import { brands, cities, departments, locations, lookupRoutes, models } from '../test/lookupData'
import { json, mockApi, type RecordedRequest } from '../test/mockApi'
import { renderWithRouter } from '../test/renderWithRouter'
import { choose } from '../test/select'

const definitionRoutes = {
  ...lookupRoutes,
  'GET /api/auth/csrf': json(200, { token: 'csrf-token' }),
  'GET /api/models': json(200, models),
  'GET /api/locations': json(200, locations),
}

function section(name: string) {
  return screen.getByRole('region', { name: new RegExp(`^${name}`) })
}

async function rowsOf(name: string) {
  const table = await within(section(name)).findByRole('table')
  return within(table)
    .getAllByRole('row')
    .slice(1)
    .map((row) =>
      within(row)
        .getAllByRole('cell')
        .map((cell) => cell.textContent)
        .filter((text) => text !== ''),
    )
}

function writes(requests: RecordedRequest[]) {
  const sent = requests.filter((r) => r.method !== 'GET')
  expect(sent.every((r) => r.headers['x-csrf-token'] === 'csrf-token')).toBe(true)
  return sent.map((r) => ({ method: r.method, path: r.path, body: r.body }))
}

describe('Marka ve Modeller', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('lists every brand and model with its state, models with their brand', async () => {
    mockApi(definitionRoutes)
    renderWithRouter('/tanimlar/marka-model')

    expect(await screen.findByRole('heading', { name: 'Marka ve Modeller', level: 1 })).toBeInTheDocument()
    expect(await rowsOf('Markalar')).toEqual([
      ['Dell', 'Aktif'],
      ['Eski Marka', 'Pasif'],
      ['HP', 'Aktif'],
    ])
    expect(await rowsOf('Modeller')).toEqual([
      ['Latitude 5440', 'Dell', 'Aktif'],
      ['Optiplex 7010', 'Dell', 'Aktif'],
      ['EliteBook 840', 'HP', 'Aktif'],
    ])
    expect(screen.getByText(/Tanımlar silinmez/)).toBeInTheDocument()
  })

  it('adds a model only under a chosen active brand', async () => {
    const requests = mockApi({
      ...definitionRoutes,
      'POST /api/models': json(201, { id: 13, name: 'Precision 3581', isActive: true, brandId: 1, brandName: 'Dell', rowVersion: 'AAAAAAAAA13=' }),
    })
    renderWithRouter('/tanimlar/marka-model')
    await rowsOf('Modeller')

    expect(within(section('Modeller')).getByRole('button', { name: 'Yeni model' })).toBeDisabled()
    await choose('Marka', 'Eski Marka (pasif)')
    expect(within(section('Modeller')).getByRole('button', { name: 'Yeni model' })).toBeDisabled()
    expect(within(section('Modeller')).getByText('Henüz model yok')).toBeInTheDocument()

    await choose('Marka', 'Dell')
    expect(await rowsOf('Modeller')).toEqual([
      ['Latitude 5440', 'Dell', 'Aktif'],
      ['Optiplex 7010', 'Dell', 'Aktif'],
    ])
    fireEvent.click(within(section('Modeller')).getByRole('button', { name: 'Yeni model' }))
    const dialog = await screen.findByRole('dialog', { name: 'Yeni model' })
    expect(within(dialog).getByText('Dell altına eklenecek.')).toBeInTheDocument()
    fireEvent.change(within(dialog).getByLabelText('Ad'), { target: { value: 'Precision 3581' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Ekle' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(writes(requests)).toEqual([{ method: 'POST', path: '/api/models', body: { name: 'Precision 3581', brandId: 1 } }])
  })

  it('renames and deactivates a brand, sending back the version it listed', async () => {
    const requests = mockApi({
      ...definitionRoutes,
      'PUT /api/brands/1': json(200, { id: 1, name: 'Dell Technologies', isActive: false, rowVersion: 'AAAAAAAAB01=' }),
    })
    renderWithRouter('/tanimlar/marka-model')
    await rowsOf('Markalar')

    fireEvent.click(screen.getByRole('button', { name: 'Dell düzenle' }))
    const dialog = await screen.findByRole('dialog', { name: 'Marka düzenle' })
    expect(within(dialog).getByLabelText('Ad')).toHaveValue('Dell')
    fireEvent.change(within(dialog).getByLabelText('Ad'), { target: { value: 'Dell Technologies' } })
    fireEvent.click(within(dialog).getByRole('switch', { name: 'Aktif' }))
    expect(within(dialog).getByText(/Yeni kayıtlarda seçilemez/)).toBeInTheDocument()
    const listed = requests.filter((r) => r.path === '/api/brands').length
    fireEvent.click(within(dialog).getByRole('button', { name: 'Kaydet' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(writes(requests)).toEqual([
      { method: 'PUT', path: '/api/brands/1', body: { name: 'Dell Technologies', isActive: false, rowVersion: brands[0].rowVersion } },
    ])
    await waitFor(() => expect(requests.filter((r) => r.path === '/api/brands').length).toBeGreaterThan(listed))
  })

  it('says so and saves nothing when someone else changed the record meanwhile', async () => {
    const requests = mockApi({
      ...definitionRoutes,
      'PUT /api/brands/3': json(409, {
        title: 'Kayıt siz düzenlerken başka bir kullanıcı tarafından değiştirildi.',
        status: 409,
        code: 'concurrency_conflict',
      }),
    })
    renderWithRouter('/tanimlar/marka-model')
    await rowsOf('Markalar')

    fireEvent.click(screen.getByRole('button', { name: 'HP düzenle' }))
    const dialog = await screen.findByRole('dialog', { name: 'Marka düzenle' })
    fireEvent.change(within(dialog).getByLabelText('Ad'), { target: { value: 'Hewlett-Packard' } })
    const listed = requests.filter((r) => r.path === '/api/brands').length
    fireEvent.click(within(dialog).getByRole('button', { name: 'Kaydet' }))

    const alert = await within(dialog).findByRole('alert')
    expect(alert).toHaveTextContent('Kayıt siz düzenlerken başka bir kullanıcı tarafından değiştirildi.')
    expect(alert).toHaveTextContent('Değişiklikleriniz kaydedilmedi.')
    expect(within(dialog).getByRole('button', { name: 'Kaydet' })).toBeDisabled()
    await waitFor(() => expect(requests.filter((r) => r.path === '/api/brands').length).toBeGreaterThan(listed))
  })

  it("shows the API's reason next to the field it is about", async () => {
    mockApi({
      ...definitionRoutes,
      'PUT /api/models/31': json(400, {
        title: 'Girilen bilgiler geçersiz.',
        status: 400,
        errors: { isActive: ['Markası pasif olan model etkinleştirilemez; önce markayı etkinleştirin.'] },
      }),
      'PUT /api/models/12': json(409, {
        title: 'Bu ad zaten kullanılıyor.',
        status: 409,
        code: 'duplicate_value',
        errors: { name: ['Bu markada aynı adla bir model zaten var.'] },
      }),
    })
    renderWithRouter('/tanimlar/marka-model')
    await rowsOf('Modeller')

    fireEvent.click(screen.getByRole('button', { name: 'EliteBook 840 düzenle' }))
    let dialog = await screen.findByRole('dialog', { name: 'Model düzenle' })
    expect(within(dialog).getByText('Marka: HP (değiştirilemez)')).toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('switch', { name: 'Aktif' }))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Kaydet' }))
    expect(await within(dialog).findByText('Markası pasif olan model etkinleştirilemez; önce markayı etkinleştirin.')).toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Vazgeç' }))

    fireEvent.click(await screen.findByRole('button', { name: 'Optiplex 7010 düzenle' }))
    dialog = await screen.findByRole('dialog', { name: 'Model düzenle' })
    fireEvent.change(within(dialog).getByLabelText('Ad'), { target: { value: 'Latitude 5440' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Kaydet' }))
    expect(await within(dialog).findByText('Bu markada aynı adla bir model zaten var.')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Ad')).toHaveAttribute('aria-invalid', 'true')
  })

  it('asks for a name before sending anything', async () => {
    const requests = mockApi(definitionRoutes)
    renderWithRouter('/tanimlar/marka-model')
    await rowsOf('Markalar')

    fireEvent.click(screen.getByRole('button', { name: 'Dell düzenle' }))
    const dialog = await screen.findByRole('dialog', { name: 'Marka düzenle' })
    fireEvent.change(within(dialog).getByLabelText('Ad'), { target: { value: '   ' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Kaydet' }))

    expect(await within(dialog).findByText('Ad zorunludur.')).toBeInTheDocument()
    expect(writes(requests)).toEqual([])
  })
})

describe('Lokasyonlar', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('lists cities, departments and every location with its city', async () => {
    mockApi(definitionRoutes)
    renderWithRouter('/tanimlar/lokasyonlar')

    expect(await screen.findByRole('heading', { name: 'Lokasyonlar', level: 1 })).toBeInTheDocument()
    expect(await rowsOf('Şehirler')).toEqual(cities.map((city) => [city.name, 'Aktif']))
    expect(await rowsOf('Departmanlar')).toEqual(departments.map((department) => [department.name, 'Aktif']))
    expect(await rowsOf('Lokasyonlar')).toEqual([
      ['Merkez Ofis', 'İstanbul', 'Aktif'],
      ['Depo', 'İstanbul', 'Pasif'],
    ])
  })

  it('reactivates a location with the version it listed', async () => {
    const requests = mockApi({
      ...definitionRoutes,
      'PUT /api/locations/61': json(200, { ...locations[1], isActive: true, rowVersion: 'AAAAAAAAB61=' }),
    })
    renderWithRouter('/tanimlar/lokasyonlar')
    await rowsOf('Lokasyonlar')

    fireEvent.click(screen.getByRole('button', { name: 'Depo düzenle' }))
    const dialog = await screen.findByRole('dialog', { name: 'Lokasyon düzenle' })
    expect(within(dialog).getByText('Şehir: İstanbul (değiştirilemez)')).toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('switch', { name: 'Pasif' }))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Kaydet' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(writes(requests)).toEqual([
      { method: 'PUT', path: '/api/locations/61', body: { name: 'Depo', isActive: true, rowVersion: locations[1].rowVersion } },
    ])
  })
})

describe('Zimmetler', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('lists only the assets assigned now, without the state and archive filters', async () => {
    const requests = mockApi({ ...lookupRoutes, 'GET /api/assets': json(200, pageOf([])) })
    renderWithRouter('/zimmetler?status=Faulty&archived=true')

    expect(await screen.findByRole('heading', { name: 'Zimmetler', level: 1 })).toBeInTheDocument()
    expect(await screen.findByText('Zimmetli demirbaş yok')).toBeInTheDocument()
    expect(requests.filter((r) => r.path.startsWith('/api/assets')).map((r) => r.path)).toEqual(['/api/assets?status=Assigned'])
    expect(screen.getByRole('link', { name: 'Zimmet hareketleri' })).toHaveAttribute('href', '/raporlar/zimmet-hareketleri')
    expect(screen.getByRole('link', { name: 'Envantere git' })).toHaveAttribute('href', '/envanter')

    const toggle = screen.queryByRole('button', { name: /^Filtreler(,|$)/ })
    if (toggle) fireEvent.click(toggle)
    expect(await screen.findByRole('combobox', { name: 'Marka' })).toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Durum' })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Arşivlenmişleri göster')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Filtreleri temizle' })).toBeDisabled()
  })
})
