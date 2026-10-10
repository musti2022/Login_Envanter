import { Box, Button, Table, TableBody, TableCell, TableHead, TableRow, Tooltip, Typography } from '@mui/material'
import { useState } from 'react'
import { formatNumber } from '../inventory/labels'
import { chartColors } from './chartColors'
import type { MonthlyMovement } from './dashboardApi'
import { dateOf, describeMonth, longMonth, scaleTop, shortMonth } from './monthlyMovements'

const series = [
  { key: 'assignedCount', label: 'Zimmet verildi', color: chartColors.assigned },
  { key: 'returnedCount', label: 'İade alındı', color: chartColors.returned },
] as const

const plotHeight = 160
/**
 * Assignments and returns of the last twelve months as pairs of bars, one month per column. Hovering or focusing a
 * month tells its numbers; the same numbers are one click away as a table.
 */
export function MonthlyMovementsChart({ months }: { months: MonthlyMovement[] }) {
  const [asTable, setAsTable] = useState(false)
  const top = scaleTop(Math.max(0, ...months.flatMap((m) => [m.assignedCount, m.returnedCount])))
  const quiet = months.every((m) => m.assignedCount + m.returnedCount === 0)

  return (
    <Box>
      <Box sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: 1, mb: 2 }}>
        <Box aria-hidden="true" sx={{ display: 'flex', gap: 2 }}>
          {series.map((s) => (
            <Box key={s.key} sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
              <Box sx={{ width: 10, height: 10, borderRadius: '2px', bgcolor: s.color }} />
              <Typography variant="caption" color="textSecondary">
                {s.label}
              </Typography>
            </Box>
          ))}
        </Box>
        <Button size="small" onClick={() => setAsTable(!asTable)} aria-pressed={asTable}>
          {asTable ? 'Grafik olarak göster' : 'Tablo olarak göster'}
        </Button>
      </Box>

      {asTable ? (
        <Table size="small" aria-label="Aylık zimmet hareketleri">
          <TableHead>
            <TableRow>
              <TableCell>Ay</TableCell>
              {series.map((s) => (
                <TableCell key={s.key} align="right">
                  {s.label}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {months.map((m) => (
              <TableRow key={m.month}>
                <TableCell>{longMonth.format(dateOf(m.month))}</TableCell>
                {series.map((s) => (
                  <TableCell key={s.key} align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>
                    {formatNumber(m[s.key])}
                  </TableCell>
                ))}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      ) : (
        <>
          {quiet && (
            <Typography variant="body2" color="textSecondary" sx={{ mb: 1 }}>
              Son 12 ayda zimmet verilmedi ve iade alınmadı.
            </Typography>
          )}
          <Box sx={{ display: 'grid', gridTemplateColumns: 'auto minmax(0, 1fr)', columnGap: 1 }}>
            {/* Scale: the top, the middle and zero. */}
            <Box aria-hidden="true" sx={{ position: 'relative', height: plotHeight, minWidth: 16 }}>
              {[top, top / 2, 0].map((value, i) => (
                <Typography
                  key={value}
                  variant="caption"
                  color="textSecondary"
                  sx={{ position: 'absolute', right: 0, top: `${i * 50}%`, transform: 'translateY(-50%)', lineHeight: 1 }}
                >
                  {formatNumber(value)}
                </Typography>
              ))}
            </Box>
            <Box sx={{ position: 'relative', height: plotHeight }}>
              {[0, 50, 100].map((offset) => (
                <Box
                  key={offset}
                  aria-hidden="true"
                  sx={{
                    position: 'absolute',
                    left: 0,
                    right: 0,
                    top: `${offset}%`,
                    borderTop: 1,
                    borderColor: offset === 100 ? 'text.disabled' : 'divider',
                  }}
                />
              ))}
              <Box role="list" aria-label="Aylık zimmet hareketleri" sx={{ position: 'absolute', inset: 0, display: 'flex' }}>
                {months.map((m) => (
                  <Tooltip key={m.month} title={describeMonth(m)} placement="top" arrow>
                    <Box
                      role="listitem"
                      tabIndex={0}
                      aria-label={describeMonth(m)}
                      sx={{
                        flex: 1,
                        display: 'flex',
                        alignItems: 'flex-end',
                        justifyContent: 'center',
                        gap: '2px',
                        borderRadius: 1,
                        outlineOffset: -2,
                        '&:hover, &:focus-visible': { bgcolor: 'action.hover' },
                      }}
                    >
                      {series.map((s) => (
                        <Box
                          key={s.key}
                          sx={{
                            width: 'min(10px, 35%)',
                            height: `${(m[s.key] / top) * 100}%`,
                            minHeight: m[s.key] > 0 ? 2 : 0,
                            borderRadius: '4px 4px 0 0',
                            bgcolor: s.color,
                          }}
                        />
                      ))}
                    </Box>
                  </Tooltip>
                ))}
              </Box>
            </Box>
            <span />
            <Box aria-hidden="true" sx={{ display: 'flex', mt: 0.5 }}>
              {months.map((m, i) => {
                const date = dateOf(m.month)
                return (
                  <Typography
                    key={m.month}
                    variant="caption"
                    color="textSecondary"
                    sx={{ flex: 1, textAlign: 'center', lineHeight: 1.3, fontSize: { xs: 11, sm: 12 }, minWidth: 0 }}
                  >
                    {shortMonth.format(date)}
                    {(i === 0 || date.getMonth() === 0) && (
                      <>
                        <br />
                        {date.getFullYear()}
                      </>
                    )}
                  </Typography>
                )
              })}
            </Box>
          </Box>
        </>
      )}
    </Box>
  )
}
