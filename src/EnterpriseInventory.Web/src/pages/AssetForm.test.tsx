import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { assetQueryKey } from '../inventory/assetsApi'
import { details } from '../test/assetData'
import { brands, lookupRoutes, models } from '../test/lookupData'
import { json, mockApi, type RecordedRequest } from '../test/mockApi'
import { renderWithRouter } from '../test/renderWithRouter'
import { choose, optionsOf } from '../test/select'

const csrf = { 'GET /api/auth/csrf': json(200, { token: 'csrf-token' }) }

function writes(requests: RecordedRequest[], method: string) {
  return requests.filter((r) => r.method === method && r.path.startsWith('/api/assets'))
}

function textbox(name: string) {
  return screen.getByRole('textbox', { name })
}

function helperTextOf(element: HTMLElement) {
  const id = element.getAttribute('aria-describedby')
  return id ? document.getElementById(id)?.textContent : undefined
}

describe('adding an asset', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('saves what the form says and opens the new asset', async () => {
    const requests = mockApi({
      ...lookupRoutes,
      ...csrf,
      'POST /api/assets': json(201, details({ id: 42, assetCode: 'DMR-0042' })),
    })
    const { router } = renderWithRouter('/envanter/yeni')
    expect(await screen.findByRole('heading', { name: 'Yeni Demirbaş' })).toBeInTheDocument()

    fireEvent.change(textbox('Demirbaş Kodu'), { target: { value: '  DMR-0042 ' } })
    await choose('Tür', 'Dizüstü')
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Marka' })).toBeInTheDocument())
    await choose('Marka', 'Dell')
    await choose('Model', 'Optiplex 7010')
    await choose('Şehir', 'İstanbul')
    await choose('Lokasyon', 'Merkez Ofis')
    await choose('Departman', 'Muhasebe')
    await choose('Durum', 'Arızalı')
    expect(helperTextOf(textbox('Seri No'))).toBe('Boşluklar kaldırılır, harfler büyük harfle kaydedilir.')
    fireEvent.change(textbox('Seri No'), { target: { value: 'SN-42' } })
    fireEvent.click(screen.getByRole('button', { name: 'Demirbaşı ekle' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/envanter/42'))
    expect(writes(requests, 'POST').map((r) => r.body)).toEqual([
      {
        assetCode: 'DMR-0042',
        assetType: 'Laptop',
        status: 'Faulty',
        modelId: 12,
        cityId: 6,
        departmentId: 9,
        locationId: 60,
        computerName: null,
        serialNumber: 'SN-42',
        description: null,
      },
    ])
    expect(writes(requests, 'POST')[0].headers['x-csrf-token']).toBe('csrf-token')
    expect(router.state.location.state).toEqual({ notice: 'DMR-0042 envantere eklendi.' })
  })

  it('checks the fields before sending anything', async () => {
    const requests = mockApi({ ...lookupRoutes, ...csrf })
    renderWithRouter('/envanter/yeni')
    await screen.findByRole('heading', { name: 'Yeni Demirbaş' })

    fireEvent.change(textbox('Bilgisayar Adı'), { target: { value: 'PC\u0007' } })
    fireEvent.click(screen.getByRole('button', { name: 'Demirbaşı ekle' }))

    expect(await screen.findByText('Demirbaş kodu zorunludur.')).toBeInTheDocument()
    for (const message of ['Demirbaş türü seçilmelidir.', 'Marka seçilmelidir.', 'Model seçilmelidir.', 'Şehir seçilmelidir.', 'Departman seçilmelidir.']) {
      expect(screen.getByText(message)).toBeInTheDocument()
    }
    expect(screen.getByText('Bilgisayar adı geçersiz karakter içeriyor.')).toBeInTheDocument()
    expect(textbox('Demirbaş Kodu')).toHaveFocus()
    expect(writes(requests, 'POST')).toEqual([])
  })

  it('offers only active lookups and lets the user choose a status but not "Zimmetli"', async () => {
    mockApi({ ...lookupRoutes, ...csrf })
    renderWithRouter('/envanter/yeni')
    await screen.findByRole('heading', { name: 'Yeni Demirbaş' })

    await waitFor(async () => expect(await optionsOf('Marka')).toEqual(['Dell', 'HP']))
    expect(await optionsOf('Durum')).toEqual(['Boşta', 'Arızalı', 'Hurda'])
    expect(screen.getByRole('combobox', { name: 'Model' })).toHaveAttribute('aria-disabled', 'true')
    await choose('Şehir', 'İstanbul')
    expect(await optionsOf('Lokasyon')).toEqual(['Seçilmedi', 'Merkez Ofis'])
  })

  it('shows the reasons the API gives under the fields they concern', async () => {
    mockApi({
      ...lookupRoutes,
      ...csrf,
      'POST /api/assets': json(409, {
        title: 'Bu bilgiler başka bir demirbaşta kullanılıyor.',
        code: 'duplicate_value',
        errors: { assetCode: ['Bu demirbaş kodu başka bir kayıtta kullanılıyor.'], serialNumber: ['Bu seri numarası başka bir kayıtta kullanılıyor.'] },
      }),
    })
    renderWithRouter('/envanter/yeni')
    await fillMinimalForm()

    fireEvent.click(screen.getByRole('button', { name: 'Demirbaşı ekle' }))

    await waitFor(() => expect(helperTextOf(textbox('Demirbaş Kodu'))).toBe('Bu demirbaş kodu başka bir kayıtta kullanılıyor.'))
    expect(helperTextOf(textbox('Seri No'))).toBe('Bu seri numarası başka bir kayıtta kullanılıyor.')
    expect(textbox('Demirbaş Kodu')).toHaveFocus()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('keeps the form and shows the error code when the server fails', async () => {
    mockApi({
      ...lookupRoutes,
      ...csrf,
      'POST /api/assets': json(500, { title: 'Beklenmeyen bir hata oluştu.', correlationId: 'corr-123' }),
    })
    const { router } = renderWithRouter('/envanter/yeni')
    await fillMinimalForm()

    fireEvent.click(screen.getByRole('button', { name: 'Demirbaşı ekle' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Beklenmeyen bir hata oluştu.')
    expect(alert).toHaveTextContent('Hata kodu: corr-123')
    expect(textbox('Demirbaş Kodu')).toHaveValue('DMR-0099')
    expect(router.state.location.pathname).toBe('/envanter/yeni')
  })

  it('adds a missing brand and model without leaving the form', async () => {
    const brandList = [...brands]
    const modelList = [...models]
    const requests = mockApi({
      ...lookupRoutes,
      ...csrf,
      'GET /api/brands': () => json(200, brandList),
      'POST /api/brands': ({ body }) => {
        const brand = { id: 4, name: (body as { name: string }).name, isActive: true }
        brandList.push(brand)
        return json(201, brand)
      },
      'GET /api/models': ({ path }) => json(200, modelList.filter((m) => path.endsWith(`brandId=${m.brandId}`))),
      'POST /api/models': ({ body }) => {
        const model = { id: 40, name: (body as { name: string }).name, isActive: true, brandId: 4, brandName: 'Lenovo' }
        modelList.push(model)
        return json(201, model)
      },
    })
    renderWithRouter('/envanter/yeni')
    await screen.findByRole('heading', { name: 'Yeni Demirbaş' })
    expect(screen.getByRole('button', { name: 'Yeni model ekle' })).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: 'Yeni marka ekle' }))
    const brandDialog = await screen.findByRole('dialog', { name: 'Yeni marka' })
    fireEvent.change(within(brandDialog).getByRole('textbox', { name: 'Ad' }), { target: { value: 'Lenovo' } })
    fireEvent.click(within(brandDialog).getByRole('button', { name: 'Ekle' }))
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Marka' })).toHaveTextContent('Lenovo'))

    fireEvent.click(screen.getByRole('button', { name: 'Yeni model ekle' }))
    const modelDialog = await screen.findByRole('dialog', { name: 'Yeni model' })
    expect(modelDialog).toHaveTextContent('Lenovo altına eklenecek.')
    fireEvent.change(within(modelDialog).getByRole('textbox', { name: 'Ad' }), { target: { value: 'ThinkPad T14' } })
    fireEvent.click(within(modelDialog).getByRole('button', { name: 'Ekle' }))
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Model' })).toHaveTextContent('ThinkPad T14'))

    expect(requests.filter((r) => r.method === 'POST').map((r) => [r.path, r.body])).toEqual([
      ['/api/brands', { name: 'Lenovo' }],
      ['/api/models', { name: 'ThinkPad T14', brandId: 4 }],
    ])
  })

  it('says why a lookup could not be added', async () => {
    mockApi({
      ...lookupRoutes,
      ...csrf,
      'POST /api/cities': json(409, { title: 'Bu ad zaten kullanılıyor.', code: 'duplicate_value', errors: { name: ['Aynı adla bir şehir zaten var.'] } }),
    })
    renderWithRouter('/envanter/yeni')
    await screen.findByRole('heading', { name: 'Yeni Demirbaş' })

    fireEvent.click(screen.getByRole('button', { name: 'Yeni şehir ekle' }))
    const dialog = await screen.findByRole('dialog', { name: 'Yeni şehir' })
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Ad' }), { target: { value: 'ankara' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Ekle' }))

    expect(await within(dialog).findByText('Aynı adla bir şehir zaten var.')).toBeInTheDocument()
  })
})

describe('editing an asset', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('saves the changes with the row version it read and opens the asset', async () => {
    const requests = mockApi({
      ...lookupRoutes,
      ...csrf,
      'GET /api/assets/1': json(200, details({ status: 'Faulty', description: 'Ekranı çizik' })),
      'PUT /api/assets/1': json(200, details({ serialNumber: 'SN-200' })),
    })
    const { router } = renderWithRouter('/envanter/1/duzenle')

    expect(await screen.findByRole('textbox', { name: 'Demirbaş Kodu' })).toHaveValue('DMR-0001')
    const save = screen.getByRole('button', { name: 'Kaydet' })
    expect(save).toBeDisabled()
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Model' })).toHaveTextContent('Latitude 5440'))
    expect(screen.getByRole('combobox', { name: 'Durum' })).toHaveTextContent('Arızalı')

    fireEvent.change(textbox('Seri No'), { target: { value: 'SN-200' } })
    fireEvent.click(save)

    await waitFor(() => expect(router.state.location.pathname).toBe('/envanter/1'))
    expect(writes(requests, 'PUT').map((r) => r.body)).toEqual([
      {
        assetCode: 'DMR-0001',
        assetType: 'Laptop',
        status: 'Faulty',
        modelId: 11,
        cityId: 6,
        departmentId: 8,
        locationId: 60,
        computerName: 'PC-IST-01',
        serialNumber: 'SN-200',
        description: 'Ekranı çizik',
        rowVersion: 'AAAAAAAAB9E=',
      },
    ])
    expect(router.state.location.state).toEqual({ notice: 'Değişiklikler kaydedildi.' })
  })

  it('never moves the edit onto a newer version silently: a conflict is shown and the user reloads', async () => {
    let version = 1
    const requests = mockApi({
      ...lookupRoutes,
      ...csrf,
      'GET /api/assets/1': () => json(200, details({ rowVersion: `v${version}`, computerName: `PC-V${version}` })),
      'PUT /api/assets/1': ({ body }) =>
        (body as { rowVersion: string }).rowVersion === `v${version}`
          ? json(200, details({ rowVersion: `v${version + 1}` }))
          : json(409, {
              title: 'Kayıt siz düzenlerken başka bir kullanıcı tarafından değiştirildi.',
              detail: 'Değişiklikleriniz kaydedilmedi.',
              code: 'concurrency_conflict',
            }),
    })
    const { router, queryClient } = renderWithRouter('/envanter/1/duzenle')
    expect(await screen.findByRole('textbox', { name: 'Bilgisayar Adı' })).toHaveValue('PC-V1')

    // Someone else saves; this page happens to fetch the asset again (since day 28, after a live notification). The
    // user is warned, and the edit stays on the version it started from.
    version = 2
    await act(() => queryClient.invalidateQueries({ queryKey: assetQueryKey(1) }))
    expect(await screen.findByText(/siz düzenlerken başka bir kullanıcı tarafından değiştirildi\. Şimdi kaydederseniz/)).toBeInTheDocument()
    expect(textbox('Bilgisayar Adı')).toHaveValue('PC-V1')

    fireEvent.change(textbox('Seri No'), { target: { value: 'SN-BENIM' } })
    fireEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

    const alert = (await screen.findByText('Kayıt siz düzenlerken başka bir kullanıcı tarafından değiştirildi.')).closest('[role="alert"]') as HTMLElement
    expect(alert).toHaveTextContent('Değişiklikleriniz kaydedilmedi.')
    expect(writes(requests, 'PUT').map((r) => (r.body as { rowVersion: string }).rowVersion)).toEqual(['v1'])
    expect(router.state.location.pathname).toBe('/envanter/1/duzenle')

    fireEvent.click(within(alert).getByRole('button', { name: 'Güncel kaydı yükle' }))

    expect(await screen.findByText('Kaydın güncel hali yüklendi. Değişikliklerinizi yeniden yapıp kaydedin.')).toBeInTheDocument()
    expect(textbox('Bilgisayar Adı')).toHaveValue('PC-V2')
    expect(textbox('Seri No')).toHaveValue('SN-100')
    fireEvent.change(textbox('Seri No'), { target: { value: 'SN-BENIM' } })
    fireEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/envanter/1'))
    expect(writes(requests, 'PUT').map((r) => (r.body as { rowVersion: string }).rowVersion)).toEqual(['v1', 'v2'])
  })

  it('keeps the status of an assigned asset and its deactivated model', async () => {
    const requests = mockApi({
      ...lookupRoutes,
      ...csrf,
      'GET /api/models': json(200, [
        { id: 11, name: 'Latitude 5440', isActive: false, brandId: 1, brandName: 'Dell' },
        { id: 12, name: 'Optiplex 7010', isActive: true, brandId: 1, brandName: 'Dell' },
        { id: 13, name: 'Eski Model', isActive: false, brandId: 1, brandName: 'Dell' },
      ]),
      'GET /api/assets/1': json(200, details({ status: 'Assigned' })),
      'PUT /api/assets/1': json(200, details({ status: 'Assigned' })),
    })
    const { router } = renderWithRouter('/envanter/1/duzenle')
    await screen.findByRole('textbox', { name: 'Demirbaş Kodu' })

    const status = screen.getByRole('combobox', { name: 'Durum' })
    expect(status).toHaveTextContent('Zimmetli')
    expect(status).toHaveAttribute('aria-disabled', 'true')
    expect(screen.getByText('Zimmetli demirbaşın durumu iade alınınca değişir.')).toBeInTheDocument()
    await waitFor(async () => expect(await optionsOf('Model')).toEqual(['Latitude 5440 (pasif)', 'Optiplex 7010']))

    fireEvent.change(textbox('Açıklama'), { target: { value: 'Yeni açıklama' } })
    fireEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/envanter/1'))
    expect(writes(requests, 'PUT')[0].body).toMatchObject({ status: 'Assigned', modelId: 11, description: 'Yeni açıklama' })
  })

  it('does not open an archived asset for editing', async () => {
    mockApi({ ...lookupRoutes, 'GET /api/assets/1': json(200, details({ isArchived: true })) })
    renderWithRouter('/envanter/1/duzenle')

    expect(await screen.findByText('Arşivlenmiş demirbaş düzenlenemez; yalnızca görüntülenebilir.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Kaydet' })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Detaya git' })).toHaveAttribute('href', '/envanter/1')
  })

  it.each([
    ['an asset that does not exist', '/envanter/7/duzenle'],
    ['an address that is not an asset', '/envanter/abc/duzenle'],
  ])('says so for %s', async (_, path) => {
    const requests = mockApi({ ...lookupRoutes, 'GET /api/assets/7': json(404, { title: 'Demirbaş bulunamadı.', code: 'asset_not_found' }) })
    renderWithRouter(path)

    expect(await screen.findByText('Demirbaş bulunamadı')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Envantere dön' })).toHaveAttribute('href', '/envanter')
    expect(requests.filter((r) => r.path.startsWith('/api/assets')).length).toBeLessThanOrEqual(1)
  })
})

async function fillMinimalForm() {
  await screen.findByRole('heading', { name: 'Yeni Demirbaş' })
  fireEvent.change(textbox('Demirbaş Kodu'), { target: { value: 'DMR-0099' } })
  await choose('Tür', 'Monitör')
  await choose('Marka', 'HP')
  await choose('Model', 'EliteBook 840')
  await choose('Şehir', 'Ankara')
  await choose('Departman', 'Bilgi İşlem')
  fireEvent.change(textbox('Seri No'), { target: { value: 'SN-99' } })
}
