import { Box, CircularProgress, Typography } from '@mui/material'

interface LoadingStateProps {
  message?: string
}

export function LoadingState({ message = 'Yükleniyor...' }: LoadingStateProps) {
  return (
    <Box role="status" aria-live="polite" sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 2, py: 8 }}>
      <CircularProgress size={28} aria-hidden="true" />
      <Typography color="text.secondary">{message}</Typography>
    </Box>
  )
}
