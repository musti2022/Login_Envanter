// Passwords, tokens and user data must never be written to browser storage (see proje_talimatlari.md).
const sources = import.meta.glob(['/src/**/*.{ts,tsx}', '!/src/**/*.test.{ts,tsx}'], {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>

describe('browser storage', () => {
  it('is not used by the application code', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(10)
    const offenders = Object.entries(sources)
      .filter(([, source]) => /\b(localStorage|sessionStorage|indexedDB|document\.cookie)\b/.test(source))
      .map(([path]) => path)

    expect(offenders).toEqual([])
  })
})
