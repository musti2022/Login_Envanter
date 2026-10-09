import { Box, Tooltip, Typography } from '@mui/material'
import type { LiveState } from './useLiveUpdates'

const shown: Record<LiveState, { label: string; hint: string; color: string }> = {
  connecting: {
    label: 'Bağlanıyor',
    hint: 'Canlı güncelleme bağlantısı kuruluyor.',
    color: 'text.disabled',
  },
  connected: {
    label: 'Canlı',
    hint: 'Diğer kullanıcıların değişiklikleri bu ekrana anında yansıyor.',
    color: 'success.main',
  },
  disconnected: {
    label: 'Canlı güncelleme yok',
    hint: 'Diğer kullanıcıların değişiklikleri otomatik görünmüyor; güncel hali görmek için sayfayı yenileyin.',
    color: 'warning.main',
  },
}

/** Read by screen readers on every width; shown as text from tablet width up, as a dot alone on a phone. */
const labelSx = {
  whiteSpace: 'nowrap',
  fontSize: '0.8125rem',
  position: { xs: 'absolute', sm: 'static' },
  width: { xs: 1, sm: 'auto' },
  height: { xs: 1, sm: 'auto' },
  overflow: { xs: 'hidden', sm: 'visible' },
  clipPath: { xs: 'inset(50%)', sm: 'none' },
} as const

/** The live connection's state in the header: whether changes made elsewhere show up on their own. */
export function LiveStatus({ state }: { state: LiveState }) {
  const { label, hint, color } = shown[state]

  return (
    <Tooltip title={hint}>
      <Box
        role="status"
        aria-label="Canlı güncelleme durumu"
        tabIndex={0}
        sx={{ display: 'flex', alignItems: 'center', gap: 0.75, px: 0.5, color: 'text.secondary', borderRadius: 1, flexShrink: 0 }}
      >
        <Box aria-hidden="true" sx={{ width: 10, height: 10, borderRadius: '50%', bgcolor: color, flexShrink: 0 }} />
        <Typography component="span" sx={labelSx}>
          {label}
        </Typography>
      </Box>
    </Tooltip>
  )
}
