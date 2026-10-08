import { Box, Typography } from '@mui/material'
import type { ReactNode } from 'react'

interface PageHeaderProps {
  title: string
  description?: string
  actions?: ReactNode
}

export function PageHeader({ title, description, actions }: PageHeaderProps) {
  return (
    <Box
      sx={{
        display: 'flex',
        flexWrap: 'wrap',
        alignItems: 'flex-start',
        justifyContent: 'space-between',
        gap: 2,
        mb: 3,
      }}
    >
      <title>{`${title} | Kurumsal Envanter`}</title>
      <Box>
        <Typography variant="h4" component="h1" color="primary" sx={{ fontWeight: 700 }}>
          {title}
        </Typography>
        {description && (
          <Typography color="textSecondary" sx={{ mt: 0.5 }}>
            {description}
          </Typography>
        )}
      </Box>
      {actions}
    </Box>
  )
}
