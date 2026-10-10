import { Box, Button, Container } from '@mui/material'
import { useEffect } from 'react'
import { useRouteError } from 'react-router'
import { ErrorState } from '../components/states/ErrorState'

/** Shown when rendering a route throws. Technical details stay in the console, never on screen. */
export function RouteErrorPage() {
  const error = useRouteError()

  useEffect(() => {
    console.error(error)
  }, [error])

  return (
    <Box sx={{ minHeight: '100vh', display: 'flex', alignItems: 'center', bgcolor: 'background.default' }}>
      <Container maxWidth="sm">
        <title>Hata | Kurumsal Envanter</title>
        <ErrorState
          title="Sayfa görüntülenemedi"
          message="Beklenmeyen bir hata oluştu. Sayfayı yenileyip tekrar deneyin; sorun devam ederse sistem yöneticisine başvurun."
          onRetry={() => window.location.reload()}
        />
        <Button href="/" sx={{ mt: 2 }}>
          Gösterge Paneline dön
        </Button>
      </Container>
    </Box>
  )
}
