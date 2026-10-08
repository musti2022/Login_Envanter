import RefreshIcon from '@mui/icons-material/Refresh'
import { Alert, AlertTitle, Button } from '@mui/material'

interface ErrorStateProps {
  title?: string
  message?: string
  onRetry?: () => void
}

export function ErrorState({
  title = 'Bir hata oluştu',
  message = 'İşlem tamamlanamadı. Lütfen daha sonra tekrar deneyin.',
  onRetry,
}: ErrorStateProps) {
  return (
    <Alert
      severity="error"
      variant="outlined"
      sx={{ bgcolor: 'background.paper' }}
      action={
        onRetry && (
          <Button color="inherit" size="small" startIcon={<RefreshIcon />} onClick={onRetry}>
            Tekrar dene
          </Button>
        )
      }
    >
      <AlertTitle>{title}</AlertTitle>
      {message}
    </Alert>
  )
}
