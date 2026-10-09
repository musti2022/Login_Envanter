import { fireEvent, screen, waitFor } from '@testing-library/react'
import { setCsrfToken } from '../api/http'
import { json, mockApi } from '../test/mockApi'
import { renderWithRouter, testUser } from '../test/renderWithRouter'

const password = 'Gizli-Parola-1'

function fillAndSubmit(userName: string, secret: string) {
  fireEvent.change(screen.getByLabelText('Kullanıcı adı'), { target: { value: userName } })
  fireEvent.change(screen.getByLabelText('Parola'), { target: { value: secret } })
  fireEvent.click(screen.getByRole('button', { name: 'Giriş Yap' }))
}

const csrf = { 'GET /api/auth/csrf': json(200, { token: 'anonymous-token' }) }

describe('LoginPage', () => {
  beforeEach(() => setCsrfToken(null))
  afterEach(() => vi.unstubAllGlobals())

  it('sends a visitor to the Turkish sign-in form', async () => {
    const { router } = renderWithRouter('/', { user: null })

    expect(await screen.findByRole('heading', { name: 'Kurumsal Envanter Yönetim Sistemi' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/giris')
    expect(screen.getByLabelText('Kullanıcı adı')).toHaveAttribute('autocomplete', 'username')
    expect(screen.getByLabelText('Parola')).toHaveAttribute('type', 'password')
    expect(screen.getByLabelText('Parola')).toHaveAttribute('autocomplete', 'current-password')
    expect(screen.getByRole('button', { name: 'Giriş Yap' })).toBeInTheDocument()
    expect(screen.queryByText('Gösterge Paneli')).not.toBeInTheDocument()
  })

  it('checks the fields before asking the server', async () => {
    const requests = mockApi(csrf)
    renderWithRouter('/giris', { user: null })

    fillAndSubmit('   ', '   ')

    expect(await screen.findByText('Kullanıcı adı zorunludur.')).toBeInTheDocument()
    expect(screen.getByText('Parola zorunludur.')).toBeInTheDocument()
    expect(requests).toHaveLength(0)
  })

  it.each([
    [401, { title: 'Kullanıcı adı veya parola hatalı.', code: 'invalid_credentials' }, 'Kullanıcı adı veya parola hatalı.', undefined],
    [
      403,
      {
        title: 'Bu uygulamaya giriş yetkiniz yok.',
        detail: 'Uygulamaya yalnızca Bim_Envanter grubunun üyeleri girebilir. Erişim için BT ekibiyle görüşün.',
        code: 'not_authorized',
      },
      'Bu uygulamaya giriş yetkiniz yok.',
      'Uygulamaya yalnızca Bim_Envanter grubunun üyeleri girebilir. Erişim için BT ekibiyle görüşün.',
    ],
    [
      503,
      { title: 'Giriş şu anda yapılamıyor.', detail: 'Kimlik doğrulama sunucusuna ulaşılamıyor.', code: 'directory_unavailable' },
      'Giriş şu anda yapılamıyor.',
      'Kimlik doğrulama sunucusuna ulaşılamıyor.',
    ],
  ])('shows the server refusal %i and clears the password', async (status, problem, title, detail) => {
    mockApi({ ...csrf, 'POST /api/auth/login': json(status, problem) })
    const { router } = renderWithRouter('/giris', { user: null })

    fillAndSubmit('mehmet.kaya', password)

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(title)
    if (detail) {
      expect(alert).toHaveTextContent(detail)
    }
    expect(screen.getByLabelText('Parola')).toHaveValue('')
    expect(screen.getByLabelText('Kullanıcı adı')).toHaveValue('mehmet.kaya')
    expect(router.state.location.pathname).toBe('/giris')
  })

  it('says how long to wait after too many attempts', async () => {
    mockApi({ ...csrf, 'POST /api/auth/login': json(429, { title: 'Çok fazla istek.' }, { 'Retry-After': '42' }) })
    renderWithRouter('/giris', { user: null })

    fillAndSubmit('ayse.yilmaz', password)

    expect(await screen.findByRole('alert')).toHaveTextContent('Lütfen 42 saniye sonra tekrar deneyin.')
  })

  it('says so when the server cannot be reached', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.reject(new TypeError('Failed to fetch'))))
    renderWithRouter('/giris', { user: null })

    fillAndSubmit('ayse.yilmaz', password)

    expect(await screen.findByRole('alert')).toHaveTextContent('Sunucuya ulaşılamıyor.')
  })

  it('shows field errors from the server', async () => {
    mockApi({
      ...csrf,
      'POST /api/auth/login': json(400, { title: 'İstek geçersiz.', errors: { userName: ['Kullanıcı adı geçersiz karakter içeriyor.'] } }),
    })
    renderWithRouter('/giris', { user: null })

    fillAndSubmit('ayse', password)

    expect(await screen.findByText('Kullanıcı adı geçersiz karakter içeriyor.')).toBeInTheDocument()
  })

  it('signs a member in with the CSRF token and opens the page they asked for, storing nothing in the browser', async () => {
    const requests = mockApi({ ...csrf, 'POST /api/auth/login': json(200, { ...testUser, csrfToken: 'session-token' }) })
    const { router } = renderWithRouter('/envanter', { user: null })
    await screen.findByLabelText('Kullanıcı adı')

    fillAndSubmit(' ayse.yilmaz ', password)

    expect(await screen.findByRole('heading', { name: 'Envanter' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/envanter')
    expect(screen.getByText('Ayşe Yılmaz')).toBeInTheDocument()

    const login = requests.find((r) => r.path === '/api/auth/login')!
    expect(login.headers['x-csrf-token']).toBe('anonymous-token')
    expect(login.body).toEqual({ userName: 'ayse.yilmaz', password })
    expect(localStorage).toHaveLength(0)
    expect(sessionStorage).toHaveLength(0)
    expect(document.cookie).not.toContain(password)
  })

  it('sends a signed-in user on to the dashboard', async () => {
    renderWithRouter('/giris')

    expect(await screen.findByRole('heading', { name: 'Gösterge Paneli' })).toBeInTheDocument()
  })
})

describe('signing out', () => {
  beforeEach(() => setCsrfToken('session-token'))
  afterEach(() => vi.unstubAllGlobals())

  it('ends the session on the server and returns to the sign-in page', async () => {
    const requests = mockApi({ 'POST /api/auth/logout': new Response(null, { status: 204 }) })
    const { router, queryClient } = renderWithRouter('/')

    fireEvent.click(screen.getAllByRole('button', { name: 'Çıkış Yap' })[0])

    await waitFor(() => expect(router.state.location.pathname).toBe('/giris'))
    // The dashboard behind it also asked for its figures; the sign-out is the only state-changing request.
    const posts = requests.filter((request) => request.method !== 'GET')
    expect(posts).toEqual([expect.objectContaining({ method: 'POST', path: '/api/auth/logout' })])
    expect(posts[0].headers['x-csrf-token']).toBe('session-token')
    expect(queryClient.getQueryData(['auth', 'currentUser'])).toBeNull()
    expect(await screen.findByRole('button', { name: 'Giriş Yap' })).toBeInTheDocument()
  })

  it('signs out here too when the server says the session has ended', async () => {
    mockApi({ 'GET /api/things': json(401, { title: 'Oturum açmanız gerekiyor.' }) })
    const { router } = renderWithRouter('/')
    const { apiFetch } = await import('../api/http')

    await apiFetch('/api/things').catch(() => undefined)

    await waitFor(() => expect(router.state.location.pathname).toBe('/giris'))
    expect(await screen.findByText('Oturumunuz sona erdi. Devam etmek için tekrar giriş yapın.')).toBeInTheDocument()
  })
})
