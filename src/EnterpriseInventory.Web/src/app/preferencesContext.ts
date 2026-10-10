import { createContext, useContext } from 'react'

export interface Preferences {
  hiddenInventoryColumns: ReadonlySet<string>
  setHiddenInventoryColumns: (hidden: ReadonlySet<string>) => void
}

export const PreferencesContext = createContext<Preferences | null>(null)

/** Screen choices kept while the app is open; see PreferencesProvider. */
export function usePreferences(): Preferences {
  const preferences = useContext(PreferencesContext)
  if (!preferences) {
    throw new Error('usePreferences must be used inside PreferencesProvider.')
  }
  return preferences
}
