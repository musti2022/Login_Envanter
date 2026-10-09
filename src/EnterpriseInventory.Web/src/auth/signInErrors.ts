import { ApiError } from '../api/http'

export interface SignInError {
  title: string
  detail?: string
}

/** A safe Turkish message for a failed sign-in; the server's own title and detail are already Turkish. */
export function describeSignInError(error: unknown): SignInError {
  if (!(error instanceof ApiError)) {
    return { title: 'Sunucuya ulaşılamıyor.', detail: 'Bağlantınızı kontrol edip tekrar deneyin.' }
  }

  if (error.status === 429) {
    const wait = error.retryAfterSeconds ? `${error.retryAfterSeconds} saniye sonra` : 'biraz sonra'
    return { title: 'Çok fazla giriş denemesi yapıldı.', detail: `Lütfen ${wait} tekrar deneyin.` }
  }

  const fallback: Record<number, string> = {
    401: 'Kullanıcı adı veya parola hatalı.',
    403: 'Bu uygulamaya giriş yetkiniz yok.',
    503: 'Giriş şu anda yapılamıyor.',
  }
  const title = error.problem?.title ?? fallback[error.status] ?? 'Giriş yapılamadı.'
  return { title, detail: error.status >= 500 && !error.problem ? 'Lütfen biraz sonra tekrar deneyin.' : error.problem?.detail }
}
