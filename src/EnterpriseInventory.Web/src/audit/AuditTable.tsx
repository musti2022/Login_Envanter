import {
  Box,
  Button,
  Link,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TablePagination,
  TableRow,
  Typography,
} from '@mui/material'
import { Link as RouterLink } from 'react-router'
import { changesOf } from '../inventory/historyChanges'
import type { PagedResult } from '../inventory/assetsApi'
import { actionLabel, formatPreciseDateTime } from '../inventory/labels'
import { pageSizes } from '../inventory/listParams'
import type { AuditLogEntry } from './auditApi'
import { assetLink, entityLabel, recordName } from './auditLabels'

/** Changes shown in a row; the rest are in the record's details. */
const shownChanges = 3

interface AuditTableProps {
  page: PagedResult<AuditLogEntry>
  onOpen: (entry: AuditLogEntry) => void
  onPage: (page: number) => void
  onPageSize: (pageSize: number) => void
}

/** Audit records, newest first: when, who, what was done to which record, and the values it changed. */
export function AuditTable({ page, onOpen, onPage, onPageSize }: AuditTableProps) {
  return (
    <>
      <TableContainer>
        <Table size="small" aria-label="Denetim kayıtları" sx={{ minWidth: 900, '& td': { verticalAlign: 'top' } }}>
          <TableHead>
            <TableRow>
              {['Zaman', 'Kullanıcı', 'İşlem', 'Kayıt', 'Değişiklikler (önce → sonra)'].map((label) => (
                <TableCell key={label} sx={{ fontWeight: 600, whiteSpace: 'nowrap' }}>
                  {label}
                </TableCell>
              ))}
              <TableCell align="right" sx={{ fontWeight: 600 }}>
                Ayrıntı
              </TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {page.items.map((entry) => {
              const changes = changesOf(entry)
              const link = assetLink(entry)
              return (
                <TableRow key={entry.id} hover>
                  <TableCell sx={{ whiteSpace: 'nowrap' }}>{formatPreciseDateTime(entry.timestamp)}</TableCell>
                  <TableCell sx={{ whiteSpace: 'nowrap' }}>{entry.userName}</TableCell>
                  <TableCell sx={{ whiteSpace: 'nowrap' }}>{actionLabel(entry.action)}</TableCell>
                  <TableCell sx={{ minWidth: 160, maxWidth: 260, overflowWrap: 'anywhere' }}>
                    <Typography variant="body2" color="textSecondary" component="span">
                      {entityLabel(entry.entityName)}{' '}
                    </Typography>
                    {link ? (
                      <Link component={RouterLink} to={link}>
                        {recordName(entry)}
                      </Link>
                    ) : (
                      recordName(entry)
                    )}
                  </TableCell>
                  <TableCell sx={{ minWidth: 280 }}>
                    <Box component="ul" sx={{ m: 0, pl: 2 }}>
                      {changes.slice(0, shownChanges).map((change) => (
                        <Typography component="li" variant="body2" key={change.field} sx={{ overflowWrap: 'anywhere' }}>
                          {change.field}: {change.text}
                        </Typography>
                      ))}
                    </Box>
                    {changes.length > shownChanges && (
                      <Typography variant="body2" color="textSecondary">
                        +{changes.length - shownChanges} alan daha
                      </Typography>
                    )}
                  </TableCell>
                  <TableCell align="right">
                    <Button
                      size="small"
                      onClick={() => onOpen(entry)}
                      aria-label={`${actionLabel(entry.action)}, ${recordName(entry)}: ayrıntı`}
                    >
                      Ayrıntı
                    </Button>
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
      </TableContainer>
      <TablePagination
        component="div"
        count={page.totalCount}
        page={page.page - 1}
        rowsPerPage={page.pageSize}
        rowsPerPageOptions={[...pageSizes]}
        onPageChange={(_, zeroBased) => onPage(zeroBased + 1)}
        onRowsPerPageChange={(event) => onPageSize(Number(event.target.value))}
        labelRowsPerPage="Sayfa başına kayıt:"
        labelDisplayedRows={({ from, to, count }) => `${from}–${to} / ${count}`}
        getItemAriaLabel={(type) =>
          ({ first: 'İlk sayfa', last: 'Son sayfa', next: 'Sonraki sayfa', previous: 'Önceki sayfa' })[type]
        }
        showFirstButton
        showLastButton
        sx={{
          '& .MuiTablePagination-toolbar': { flexWrap: 'wrap', justifyContent: 'flex-end', rowGap: 0.5, px: { xs: 1, sm: 2 } },
          '& .MuiTablePagination-spacer': { display: { xs: 'none', sm: 'block' } },
          '& .MuiTablePagination-actions': { ml: { xs: 1, sm: 2.5 } },
        }}
      />
    </>
  )
}
