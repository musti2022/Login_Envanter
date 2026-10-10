import { Box, Link, Typography } from '@mui/material'
import { Link as RouterLink } from 'react-router'
import { formatNumber } from '../inventory/labels'
import { chartColors } from './chartColors'

export interface DistributionRow {
  key: string | number
  name: string
  count: number
  /** When given, the bar shows the assigned part of the count. */
  assignedCount?: number
  /** The inventory filtered to this row; none for a row such as "Diğer markalar". */
  to?: string
}

interface DistributionListProps {
  /** Names the list for screen readers, e.g. "Şehirlere göre demirbaş sayısı". */
  label: string
  rows: DistributionRow[]
}

/**
 * One bar per row, largest first. The numbers are written beside every bar, so the bar only shows proportion; with
 * assigned counts the bar is split into the assigned part and the rest, with a legend above.
 */
export function DistributionList({ label, rows }: DistributionListProps) {
  const largest = Math.max(1, ...rows.map((row) => row.count))
  const split = rows.some((row) => row.assignedCount !== undefined)

  return (
    <>
      {split && <Legend />}
      <Box
        component="ul"
        aria-label={label}
        sx={{ listStyle: 'none', m: 0, p: 0, display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 1.5 }}
      >
        {rows.map((row) => {
          const assigned = row.assignedCount ?? 0
          return (
            <Box component="li" key={row.key}>
              <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 2, mb: 0.5 }}>
                <Typography variant="body2" noWrap title={row.name} sx={{ minWidth: 0 }}>
                  {row.to ? (
                    <Link component={RouterLink} to={row.to} color="inherit" underline="hover">
                      {row.name}
                    </Link>
                  ) : (
                    row.name
                  )}
                </Typography>
                <Typography variant="body2" sx={{ whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}>
                  {row.assignedCount !== undefined && (
                    <Typography component="span" variant="body2" color="textSecondary">
                      {formatNumber(assigned)} zimmetli ·{' '}
                    </Typography>
                  )}
                  <Box component="span" sx={{ fontWeight: 600 }}>
                    {formatNumber(row.count)}
                  </Box>
                </Typography>
              </Box>
              <Box aria-hidden="true" sx={{ height: 8, borderRadius: 4, bgcolor: 'action.hover', overflow: 'hidden' }}>
                <Box sx={{ display: 'flex', gap: '2px', height: '100%', width: `${(row.count / largest) * 100}%`, minWidth: 4 }}>
                  {split ? (
                    <>
                      {assigned > 0 && <Segment color={chartColors.assigned} grow={assigned} />}
                      {row.count - assigned > 0 && <Segment color={chartColors.rest} grow={row.count - assigned} />}
                    </>
                  ) : (
                    <Segment color={chartColors.assigned} grow={1} />
                  )}
                </Box>
              </Box>
            </Box>
          )
        })}
      </Box>
    </>
  )
}

function Segment({ color, grow }: { color: string; grow: number }) {
  return <Box sx={{ flexGrow: grow, flexBasis: 0, minWidth: 4, borderRadius: 4, bgcolor: color }} />
}

function Legend() {
  return (
    <Box aria-hidden="true" sx={{ display: 'flex', gap: 2, mb: 1.5 }}>
      {[
        { label: 'Zimmetli', color: chartColors.assigned },
        { label: 'Zimmetsiz', color: chartColors.rest },
      ].map((item) => (
        <Box key={item.label} sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
          <Box sx={{ width: 10, height: 10, borderRadius: '2px', bgcolor: item.color }} />
          <Typography variant="caption" color="textSecondary">
            {item.label}
          </Typography>
        </Box>
      ))}
    </Box>
  )
}
