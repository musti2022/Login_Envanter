import {
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Link,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material'
import type { ReactNode } from 'react'
import { Link as RouterLink } from 'react-router'
import { fieldsOf } from '../inventory/historyChanges'
import { actionLabel, formatPreciseDateTime } from '../inventory/labels'
import type { AuditLogEntry } from './auditApi'
import { assetLink, entityLabel, recordName } from './auditLabels'

interface AuditEntryDialogProps {
  entry: AuditLogEntry | null
  onClose: () => void
  /** Lists every record the same request wrote (an edit can write several, one per kind of change). */
  onShowRequest: (correlationId: string) => void
}

/** One audit record in full: every field it records, before and after, and where it came from. */
export function AuditEntryDialog({ entry, onClose, onShowRequest }: AuditEntryDialogProps) {
  const theme = useTheme()
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'))
  if (entry === null) return null

  const fields = fieldsOf(entry)
  const link = assetLink(entry)
  const recordValue = link ? (
    <Link component={RouterLink} to={link} onClick={onClose}>
      {recordName(entry)}
    </Link>
  ) : (
    recordName(entry)
  )
  const facts: { term: string; value: ReactNode }[] = [
    { term: 'Zaman', value: formatPreciseDateTime(entry.timestamp) },
    { term: 'Kullanıcı', value: entry.userName },
    { term: 'İşlem', value: actionLabel(entry.action) },
    { term: 'Kayıt', value: recordValue },
    { term: 'Kayıt türü ve no', value: `${entityLabel(entry.entityName)} #${entry.entityId}` },
    { term: 'İşlem numarası', value: entry.correlationId },
  ]

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="md" fullScreen={fullScreen} aria-labelledby="audit-entry-title">
      <DialogTitle id="audit-entry-title">
        {actionLabel(entry.action)}: {entityLabel(entry.entityName)} {recordName(entry)}
      </DialogTitle>
      <DialogContent dividers>
        <Box
          component="dl"
          sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '160px 1fr' }, columnGap: 2, rowGap: 0.5, m: 0, mb: 2 }}
        >
          {facts.map(({ term, value }) => (
            <Box key={term} sx={{ display: 'contents' }}>
              <Typography component="dt" variant="body2" color="textSecondary">
                {term}
              </Typography>
              <Typography component="dd" variant="body2" sx={{ m: 0, mb: { xs: 1, sm: 0 }, overflowWrap: 'anywhere' }}>
                {value}
              </Typography>
            </Box>
          ))}
        </Box>
        {fields.length === 0 ? (
          <Typography color="textSecondary">Bu kayıtta gösterilecek alan yok.</Typography>
        ) : (
          <TableContainer>
            <Table size="small" aria-label="Önceki ve yeni değerler">
              <TableHead>
                <TableRow>
                  <TableCell sx={{ fontWeight: 600 }}>Alan</TableCell>
                  <TableCell sx={{ fontWeight: 600 }}>Önceki değer</TableCell>
                  <TableCell sx={{ fontWeight: 600 }}>Yeni değer</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {fields.map((field) => (
                  <TableRow key={field.field} selected={field.changed && entry.oldValues !== null && entry.newValues !== null}>
                    <TableCell component="th" scope="row">
                      {field.field}
                    </TableCell>
                    <TableCell sx={{ overflowWrap: 'anywhere' }}>{field.before}</TableCell>
                    <TableCell sx={{ overflowWrap: 'anywhere' }}>{field.after}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </DialogContent>
      <DialogActions sx={{ flexWrap: 'wrap', gap: 1 }}>
        <Button
          onClick={() => {
            onShowRequest(entry.correlationId)
            onClose()
          }}
        >
          Bu işlemin tüm kayıtları
        </Button>
        <Button variant="contained" onClick={onClose}>
          Kapat
        </Button>
      </DialogActions>
    </Dialog>
  )
}
