import { Box, List, ListItemButton, ListItemIcon, ListItemText, Toolbar, Typography } from '@mui/material'
import { NavLink } from 'react-router'
import { brandColors } from '../app/theme'
import { navigationItems } from './navigation'

interface SidebarContentProps {
  onNavigate?: () => void
}

export function SidebarContent({ onNavigate }: SidebarContentProps) {
  return (
    <Box sx={{ height: '100%', bgcolor: brandColors.navy, color: brandColors.white }}>
      <Toolbar sx={{ px: 2.5 }}>
        <Typography variant="h6" component="div" noWrap sx={{ fontWeight: 700 }}>
          Kurumsal Envanter
        </Typography>
      </Toolbar>
      <Box component="nav" aria-label="Ana menü">
        <List sx={{ px: 1.5 }}>
          {navigationItems.map((item) => (
            <ListItemButton
              key={item.path}
              component={NavLink}
              to={item.path}
              end={item.path === '/'}
              onClick={onNavigate}
              sx={{
                borderRadius: 1,
                mb: 0.5,
                color: 'inherit',
                '& .MuiListItemIcon-root': { color: 'inherit', minWidth: 40 },
                '&:hover': { bgcolor: 'rgba(255, 255, 255, 0.08)' },
                '&.active': { bgcolor: brandColors.blue },
                '&.active:hover': { bgcolor: brandColors.blue },
              }}
            >
              <ListItemIcon>{item.icon}</ListItemIcon>
              <ListItemText primary={item.label} />
            </ListItemButton>
          ))}
        </List>
      </Box>
    </Box>
  )
}
