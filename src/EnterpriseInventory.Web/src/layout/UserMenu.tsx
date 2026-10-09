import AccountCircle from '@mui/icons-material/AccountCircle'
import LogoutIcon from '@mui/icons-material/Logout'
import { Box, Button, CircularProgress, IconButton, Tooltip, Typography } from '@mui/material'
import { useNavigate } from 'react-router'
import { signInPath } from '../auth/RequireAuth'
import { useCurrentUser, useSignOut } from '../auth/useAuth'

/** The signed-in user's name and the sign-out button, on the right of the header. */
export function UserMenu() {
  const { data: user } = useCurrentUser()
  const signOut = useSignOut()
  const navigate = useNavigate()

  if (!user) {
    return null
  }

  const onSignOut = () =>
    signOut.mutate(undefined, { onSettled: () => void navigate(signInPath, { replace: true }) })

  const busy = signOut.isPending
  const icon = busy ? <CircularProgress size={18} color="inherit" aria-hidden="true" /> : <LogoutIcon />

  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, ml: 'auto', minWidth: 0 }}>
      <AccountCircle color="action" aria-hidden="true" sx={{ display: { xs: 'none', sm: 'block' } }} />
      <Typography noWrap sx={{ display: { xs: 'none', sm: 'block' }, maxWidth: 240 }} title={user.userName}>
        {user.displayName}
      </Typography>
      <Button
        color="primary"
        variant="outlined"
        size="small"
        startIcon={icon}
        onClick={onSignOut}
        disabled={busy}
        sx={{ display: { xs: 'none', sm: 'inline-flex' }, ml: 1 }}
      >
        Çıkış Yap
      </Button>
      <Tooltip title="Çıkış Yap">
        <span>
          <IconButton
            aria-label="Çıkış Yap"
            onClick={onSignOut}
            disabled={busy}
            sx={{ display: { xs: 'inline-flex', sm: 'none' } }}
          >
            {icon}
          </IconButton>
        </span>
      </Tooltip>
    </Box>
  )
}
