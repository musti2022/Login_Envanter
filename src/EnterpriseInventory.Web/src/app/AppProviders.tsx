import { CssBaseline, ThemeProvider } from '@mui/material'
import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { SessionWatcher } from '../auth/SessionWatcher'
import { createQueryClient } from './queryClient'
import { theme } from './theme'

interface AppProvidersProps {
  children: ReactNode
  /** Tests pass their own client to start with known data. */
  queryClient?: QueryClient
}

export function AppProviders({ children, queryClient: givenClient }: AppProvidersProps) {
  const [queryClient] = useState(() => givenClient ?? createQueryClient())

  return (
    <QueryClientProvider client={queryClient}>
      <SessionWatcher />
      <ThemeProvider theme={theme}>
        <CssBaseline />
        {children}
      </ThemeProvider>
    </QueryClientProvider>
  )
}
