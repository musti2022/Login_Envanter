import { useState, type ReactNode } from 'react'
import { defaultHiddenColumns } from '../inventory/columns'
import { PreferencesContext } from './preferencesContext'

/**
 * Screen choices (such as the inventory's hidden columns) kept while the app is open. In memory only: nothing
 * is written to browser storage, so a reload brings back the defaults.
 */
export function PreferencesProvider({ children }: { children: ReactNode }) {
  const [hiddenInventoryColumns, setHiddenInventoryColumns] = useState(defaultHiddenColumns)
  return (
    <PreferencesContext.Provider value={{ hiddenInventoryColumns, setHiddenInventoryColumns }}>{children}</PreferencesContext.Provider>
  )
}
