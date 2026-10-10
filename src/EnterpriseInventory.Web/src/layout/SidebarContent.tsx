import { Box, List, ListItemButton, ListItemIcon, ListItemText, Toolbar, Typography } from '@mui/material'
import { NavLink } from 'react-router'
import { brandColors } from '../app/theme'
import { navigationItems } from './navigation'

interface SidebarContentProps {
  onNavigate?: () => void
}

/** Sidebar body. The navy background and white text come from the Drawer paper (see AppLayout). */
export function SidebarContent({ onNavigate }: SidebarContentProps) {
  return (
    <>
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
                borderLeft: '4px solid transparent',
                '& .MuiListItemIcon-root': { color: 'inherit', minWidth: 40 },
                '&:hover': { bgcolor: 'rgba(255, 255, 255, 0.08)' },
                // Current page: blue fill plus a white edge and bold text, so it is not signalled by colour alone.
                '&.active, &.active:hover': {
                  bgcolor: brandColors.blue,
                  borderLeftColor: brandColors.white,
                  '& .MuiListItemText-primary': { fontWeight: 700 },
                },
                '&.Mui-focusVisible': {
                  outline: `2px solid ${brandColors.white}`,
                  outlineOffset: -2,
                },
                '&.Mui-focusVisible:not(.active)': { bgcolor: 'rgba(255, 255, 255, 0.16)' },
              }}
            >
              <ListItemIcon>{item.icon}</ListItemIcon>
              <ListItemText primary={item.label} />
            </ListItemButton>
          ))}
        </List>
      </Box>
    </>
  )
}
