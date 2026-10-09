import MenuIcon from '@mui/icons-material/Menu'
import { AppBar, Box, Drawer, IconButton, Link, Toolbar, Typography, useMediaQuery } from '@mui/material'
import { useTheme } from '@mui/material/styles'
import { useState } from 'react'
import { Outlet } from 'react-router'
import { brandColors } from '../app/theme'
import { LiveStatus } from '../realtime/LiveStatus'
import { useLiveUpdates } from '../realtime/useLiveUpdates'
import { SidebarContent } from './SidebarContent'
import { UserMenu } from './UserMenu'

export const drawerWidth = 260

const drawerPaperSx = {
  width: drawerWidth,
  border: 0,
  bgcolor: brandColors.navy,
  color: brandColors.white,
} as const

export function AppLayout() {
  const theme = useTheme()
  const isDesktop = useMediaQuery(theme.breakpoints.up('md'))
  const [mobileOpen, setMobileOpen] = useState(false)
  const liveState = useLiveUpdates()

  const closeMobileDrawer = () => setMobileOpen(false)

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh' }}>
      <Link
        href="#main-content"
        sx={{
          position: 'absolute',
          left: 8,
          top: -48,
          zIndex: (theme) => theme.zIndex.tooltip,
          p: 1,
          bgcolor: 'background.paper',
          '&:focus': { top: 8 },
        }}
      >
        İçeriğe geç
      </Link>

      <AppBar
        position="fixed"
        sx={{
          width: { md: `calc(100% - ${drawerWidth}px)` },
          ml: { md: `${drawerWidth}px` },
          bgcolor: 'background.paper',
          borderBottom: 1,
          borderColor: 'divider',
        }}
      >
        <Toolbar>
          <IconButton
            edge="start"
            aria-label="Menüyü aç"
            onClick={() => setMobileOpen(true)}
            sx={{ mr: 2, display: { md: 'none' } }}
          >
            <MenuIcon />
          </IconButton>
          <Typography variant="h6" component="div" color="primary" noWrap sx={{ fontWeight: 600 }}>
            Kurumsal Envanter Yönetim Sistemi
          </Typography>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, ml: 'auto', minWidth: 0 }}>
            <LiveStatus state={liveState} />
            <UserMenu />
          </Box>
        </Toolbar>
      </AppBar>

      <Box sx={{ width: { md: drawerWidth }, flexShrink: { md: 0 } }}>
        {/* The mobile drawer is a modal; it must not stay open (scroll lock, aria-hidden app) once the
            permanent drawer takes over at the md breakpoint, e.g. after rotating a tablet. */}
        <Drawer
          variant="temporary"
          open={mobileOpen && !isDesktop}
          onClose={closeMobileDrawer}
          slotProps={{ paper: { 'aria-label': 'Menü' } }}
          sx={{ display: { xs: 'block', md: 'none' }, '& .MuiDrawer-paper': drawerPaperSx }}
        >
          <SidebarContent onNavigate={closeMobileDrawer} />
        </Drawer>
        <Drawer
          variant="permanent"
          open
          sx={{ display: { xs: 'none', md: 'block' }, '& .MuiDrawer-paper': drawerPaperSx }}
        >
          <SidebarContent />
        </Drawer>
      </Box>

      <Box
        component="main"
        id="main-content"
        tabIndex={-1}
        sx={{ flexGrow: 1, minWidth: 0, p: { xs: 2, sm: 3 }, bgcolor: 'background.default', outline: 'none' }}
      >
        <Toolbar />
        <Outlet />
      </Box>
    </Box>
  )
}
