import MenuIcon from '@mui/icons-material/Menu'
import { AppBar, Box, Drawer, IconButton, Link, Toolbar, Typography } from '@mui/material'
import { useState } from 'react'
import { Outlet } from 'react-router'
import { SidebarContent } from './SidebarContent'

export const drawerWidth = 260

export function AppLayout() {
  const [mobileOpen, setMobileOpen] = useState(false)

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
        </Toolbar>
      </AppBar>

      <Box component="aside" sx={{ width: { md: drawerWidth }, flexShrink: { md: 0 } }}>
        <Drawer
          variant="temporary"
          open={mobileOpen}
          onClose={closeMobileDrawer}
          sx={{ display: { xs: 'block', md: 'none' }, '& .MuiDrawer-paper': { width: drawerWidth, border: 0 } }}
        >
          <SidebarContent onNavigate={closeMobileDrawer} />
        </Drawer>
        <Drawer
          variant="permanent"
          open
          sx={{ display: { xs: 'none', md: 'block' }, '& .MuiDrawer-paper': { width: drawerWidth, border: 0 } }}
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
