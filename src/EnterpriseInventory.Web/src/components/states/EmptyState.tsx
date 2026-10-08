import InboxIcon from '@mui/icons-material/Inbox'
import { Box, Typography } from '@mui/material'
import type { ReactNode } from 'react'

interface EmptyStateProps {
  title: string
  description?: string
  icon?: ReactNode
  action?: ReactNode
}

export function EmptyState({ title, description, icon, action }: EmptyStateProps) {
  return (
    <Box sx={{ textAlign: 'center', py: 8, px: 2, color: 'text.secondary' }}>
      <Box aria-hidden="true" sx={{ fontSize: 48, lineHeight: 1, mb: 1, '& svg': { fontSize: 'inherit' } }}>
        {icon ?? <InboxIcon />}
      </Box>
      <Typography variant="h6" component="p" color="text.primary">
        {title}
      </Typography>
      {description && <Typography sx={{ mt: 1, maxWidth: 520, mx: 'auto' }}>{description}</Typography>}
      {action && <Box sx={{ mt: 3 }}>{action}</Box>}
    </Box>
  )
}
