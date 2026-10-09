import { HttpClient, HttpError, HttpResponse, type HttpRequest } from '@microsoft/signalr'
import { setCsrfToken } from '../api/http'
import { json, mockApi } from '../test/mockApi'
import { CsrfHttpClient } from './inventoryHub'

/** Records what the live connection would send, and answers with what each test scripts. */
class RecordingHttpClient extends HttpClient {
  readonly sent: HttpRequest[] = []
  answer: (request: HttpRequest) => HttpResponse = () => new HttpResponse(200, 'OK', '{}')

  override send(request: HttpRequest): Promise<HttpResponse> {
    this.sent.push(request)
    try {
      return Promise.resolve(this.answer(request))
    } catch (error) {
      return Promise.reject(error)
    }
  }
}

describe('The live connection’s requests', () => {
  beforeEach(() => setCsrfToken(null))
  afterEach(() => vi.unstubAllGlobals())

  it('carry the CSRF token when they change state, as API requests do', async () => {
    const api = mockApi({ 'GET /api/auth/csrf': json(200, { token: 'token-1' }) })
    const inner = new RecordingHttpClient()
    const client = new CsrfHttpClient(inner)

    await client.send({ method: 'POST', url: '/hubs/inventory/negotiate?negotiateVersion=1' })
    await client.send({ method: 'GET', url: '/hubs/inventory?id=abc' })
    await client.send({ method: 'DELETE', url: '/hubs/inventory?id=abc' })

    expect(inner.sent.map((r) => `${r.method} ${r.headers?.['X-CSRF-TOKEN'] ?? '-'}`)).toEqual(['POST token-1', 'GET -', 'DELETE token-1'])
    expect(api).toHaveLength(1)
  })

  it('get a new token once when the API refuses the one in hand', async () => {
    mockApi({ 'GET /api/auth/csrf': json(200, { token: 'fresh-token' }) })
    setCsrfToken('stale-token')
    const inner = new RecordingHttpClient()
    inner.answer = (request) => {
      if (request.headers?.['X-CSRF-TOKEN'] === 'stale-token') {
        throw new HttpError('{"title":"Güvenlik doğrulaması başarısız oldu.","code":"csrf_invalid"}', 400)
      }
      return new HttpResponse(200, 'OK', '{}')
    }

    await new CsrfHttpClient(inner).send({ method: 'POST', url: '/hubs/inventory/negotiate' })

    expect(inner.sent.map((r) => r.headers?.['X-CSRF-TOKEN'])).toEqual(['stale-token', 'fresh-token'])
  })

  it('pass other refusals on, so the connection knows it was refused', async () => {
    setCsrfToken('token-1')
    const inner = new RecordingHttpClient()
    inner.answer = () => {
      throw new HttpError('Unauthorized', 401)
    }

    await expect(new CsrfHttpClient(inner).send({ method: 'POST', url: '/hubs/inventory/negotiate' })).rejects.toMatchObject({ statusCode: 401 })
    expect(inner.sent).toHaveLength(1)
  })
})
