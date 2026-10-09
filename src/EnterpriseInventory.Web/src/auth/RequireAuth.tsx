import { Box } from '@mui/material'
import { Navigate, Outlet, useLocation } from 'react-router'
import { ErrorState } from '../components/states/ErrorState'
import { LoadingState } from '../components/states/LoadingState'
import { useCurrentUser } from './useAuth'

export const signInPath = '/giris'

/**
 * Shows its child routes only to a signed-in user; anyone else is sent to the sign-in page and brought back
 * afterwards. This only decides what to render: every API endpoint checks the session and the role itself.
 */
export function RequireAuth() {
  const location = useLocation()
  const { data: user, isPending, isError, refetch } = useCurrentUser()

  if (isPending) {
    return <LoadingState message="Oturum kontrol ediliyor..." />
  }

  if (isError) {
    return (
      <Box sx={{ p: 3 }}>
        <ErrorState
          title="Oturum bilgisi alınamadı"
          message="Sunucuya ulaşılamıyor. Lütfen biraz sonra tekrar deneyin."
          onRetry={() => void refetch()}
        />
      </Box>
    )
  }

  if (!user) {
    return <Navigate to={signInPath} replace state={{ from: location }} />
  }

  return <Outlet />
}
