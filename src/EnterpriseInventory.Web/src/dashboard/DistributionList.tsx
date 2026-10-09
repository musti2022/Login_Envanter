import { Box, Typography } from '@mui/material'
import { brandColors } from '../app/theme'
import { formatNumber } from '../inventory/labels'
import type { DistributionItem } from './dashboardApi'

interface DistributionListProps {
  /** Names the list for screen readers, e.g. "Şehirlere göre demirbaş sayısı". */
  label: string
  items: DistributionItem[]
}

/**
 * One bar per city or department, largest first. The number is written beside every bar, so the bar only
 * shows proportion; screen readers get the list of names and numbers.
 */
export function DistributionList({ label, items }: DistributionListProps) {
  const largest = Math.max(1, ...items.map((item) => item.count))

  return (
    <Box component="ul" aria-label={label} sx={{ listStyle: 'none', m: 0, p: 0, display: 'grid', gap: 1.5 }}>
      {items.map((item) => (
        <Box component="li" key={item.id}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 2, mb: 0.5 }}>
            <Typography variant="body2" noWrap title={item.name}>
              {item.name}
            </Typography>
            <Typography variant="body2" sx={{ fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}>
              {formatNumber(item.count)}
            </Typography>
          </Box>
          <Box aria-hidden="true" sx={{ height: 8, borderRadius: 4, bgcolor: 'action.hover', overflow: 'hidden' }}>
            <Box
              sx={{
                height: '100%',
                width: `${(item.count / largest) * 100}%`,
                minWidth: 4,
                borderRadius: 4,
                bgcolor: brandColors.blue,
              }}
            />
          </Box>
        </Box>
      ))}
    </Box>
  )
}
