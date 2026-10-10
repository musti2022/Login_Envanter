import { ApiError, apiFetch, fileNameOf, inBackground, onUnauthorized, setCsrfToken } from './http'
import { json, mockApi } from '../test/mockApi'

describe('apiFetch', () => {
  beforeEach(() => setCsrfToken(null))
  afterEach(() => vi.unstubAllGlobals())

  it('sends reads with the session cookie and without a CSRF token', async () => {
    const requests = mockApi({ 'GET /api/things': json(200, { ok: true }) })

    await expect(apiFetch('/api/things')).resolves.toEqual({ ok: true })

    expect(requests).toHaveLength(1)
    expect(requests[0].headers['x-csrf-token']).toBeUndefined()
    expect(vi.mocked(fetch).mock.calls[0][1]?.credentials).toBe('same-origin')
  })

  it('fetches a CSRF token once and sends it with every state-changing request', async () => {
    const requests = mockApi({
      'GET /api/auth/csrf': json(200, { token: 'token-1' }),
      'POST /api/things': json(200, {}),
      'DELETE /api/things/1': new Response(null, { status: 204 }),
    })

    await apiFetch('/api/things', { method: 'POST', body: { name: 'x' } })
    await apiFetch('/api/things/1', { method: 'DELETE' })

    expect(requests.map((r) => `${r.method} ${r.path}`)).toEqual([
      'GET /api/auth/csrf',
      'POST /api/things',
      'DELETE /api/things/1',
    ])
    expect(requests[1].headers['x-csrf-token']).toBe('token-1')
    expect(requests[1].headers['content-type']).toBe('application/json')
    expect(requests[1].body).toEqual({ name: 'x' })
    expect(requests[2].headers['x-csrf-token']).toBe('token-1')
  })

  it('gets a new token and retries once when the server rejects the token', async () => {
    let tokens = 0
    const requests = mockApi({
      'GET /api/auth/csrf': () => json(200, { token: `token-${++tokens}` }),
      'POST /api/things': (request) =>
        request.headers['x-csrf-token'] === 'token-1'
          ? json(200, { saved: true })
          : json(400, { title: 'Güvenlik doğrulaması başarısız oldu.', code: 'csrf_invalid' }),
    })
    setCsrfToken('stale-token')

    await expect(apiFetch('/api/things', { method: 'POST' })).resolves.toEqual({ saved: true })

    const posts = requests.filter((r) => r.method === 'POST')
    expect(posts.map((r) => r.headers['x-csrf-token'])).toEqual(['stale-token', 'token-1'])
  })

  it('retries only once', async () => {
    const requests = mockApi({
      'GET /api/auth/csrf': json(200, { token: 'token-1' }),
      'POST /api/things': json(400, { title: 'Güvenlik doğrulaması başarısız oldu.', code: 'csrf_invalid' }),
    })

    await expect(apiFetch('/api/things', { method: 'POST' })).rejects.toMatchObject({ status: 400, code: 'csrf_invalid' })

    expect(requests.filter((r) => r.method === 'POST')).toHaveLength(2)
  })

  it('reports an ended session, unless the caller expects 401', async () => {
    mockApi({ 'GET /api/things': json(401, { title: 'Oturum açmanız gerekiyor.' }) })
    const ended = vi.fn()
    const remove = onUnauthorized(ended)

    await expect(apiFetch('/api/things')).rejects.toBeInstanceOf(ApiError)
    await expect(apiFetch('/api/things', { ignoreUnauthorized: true })).rejects.toBeInstanceOf(ApiError)

    expect(ended).toHaveBeenCalledTimes(1)
    remove()
  })

  it('turns problem responses into ApiError with the code and Retry-After', async () => {
    mockApi({
      'GET /api/things': json(429, { title: 'Çok fazla istek.', code: 'rate_limited' }, { 'Retry-After': '30' }),
    })

    const error = await apiFetch('/api/things').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status: 429, code: 'rate_limited', retryAfterSeconds: 30, message: 'Çok fazla istek.' })
  })

  it('marks the reads started inside inBackground, and only those, as background requests', async () => {
    const requests = mockApi({
      'GET /api/auth/csrf': json(200, { token: 'token-1' }),
      'GET /api/things': json(200, {}),
      'POST /api/things': json(200, {}),
    })

    const background = inBackground(() => apiFetch('/api/things'))
    const write = inBackground(() => apiFetch('/api/things', { method: 'POST' }))
    await Promise.all([background, write, apiFetch('/api/things')])

    const marked = requests.map((r) => `${r.method} ${r.path} ${r.headers['x-background-request'] ?? '-'}`)
    // A write is always the user's own doing; the read made after inBackground returned is too.
    expect(marked).toEqual(['GET /api/things 1', 'GET /api/auth/csrf -', 'GET /api/things -', 'POST /api/things -'])
  })
})

describe('fileNameOf', () => {
  it.each([
    ["attachment; filename=envanter-2026-10-10.xlsx; filename*=UTF-8''envanter-2026-10-10.xlsx", 'envanter-2026-10-10.xlsx'],
    ["attachment; filename=rapor.xlsx; filename*=UTF-8''%C5%9Fehir-raporu.xlsx", 'şehir-raporu.xlsx'],
    ['attachment; filename="envanter arsiv.xlsx"', 'envanter arsiv.xlsx'],
    ['attachment; filename=envanter.xlsx', 'envanter.xlsx'],
    ["attachment; filename=yedek.xlsx; filename*=UTF-8''%E0%A4%A", 'yedek.xlsx'],
    ['attachment', null],
    [null, null],
  ])('reads %s as %s', (header, name) => {
    expect(fileNameOf(header)).toBe(name)
  })
})
