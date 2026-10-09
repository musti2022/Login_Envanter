import { act } from '@testing-library/react'

/** Controllable window.matchMedia so MUI's useMediaQuery can see a breakpoint change. */
export function mockViewport(initiallyDesktop: boolean) {
  let desktop = initiallyDesktop
  const listeners = new Set<() => void>()
  const original = window.matchMedia

  window.matchMedia = (query: string) =>
    ({
      media: query,
      get matches() {
        return desktop
      },
      addEventListener: (_: string, listener: () => void) => listeners.add(listener),
      removeEventListener: (_: string, listener: () => void) => listeners.delete(listener),
      addListener: (listener: () => void) => listeners.add(listener),
      removeListener: (listener: () => void) => listeners.delete(listener),
      onchange: null,
      dispatchEvent: () => true,
    }) as unknown as MediaQueryList

  return {
    setDesktop(value: boolean) {
      desktop = value
      act(() => listeners.forEach((listener) => listener()))
    },
    restore() {
      window.matchMedia = original
    },
  }
}
