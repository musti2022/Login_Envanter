import AddIcon from '@mui/icons-material/Add'
import EditIcon from '@mui/icons-material/Edit'
import {
  Box,
  Button,
  Card,
  Chip,
  IconButton,
  MenuItem,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material'
import type { UseQueryResult } from '@tanstack/react-query'
import { useState } from 'react'
import { EmptyState } from '../components/states/EmptyState'
import { ErrorState } from '../components/states/ErrorState'
import { LoadingState } from '../components/states/LoadingState'
import { AddLookupDialog, type NewLookup } from '../inventory/AddLookupDialog'
import { lookupLabel, type LookupItem } from '../inventory/lookupsApi'
import { EditLookupDialog } from './EditLookupDialog'

/** Models belong to a brand and locations to a city: the column, the filter and the parent of a new one. */
export interface LookupParent<T> {
  /** "Marka" or "Şehir". */
  label: string
  /** "markasının" or "şehrinin", for "Dell markasının modeli". */
  possessive: string
  field: 'brandId' | 'cityId'
  options: LookupItem[]
  idOf: (item: T) => number
  nameOf: (item: T) => string
}

interface LookupSectionProps<T extends LookupItem> {
  /** "Markalar". */
  title: string
  /** "marka": "Yeni marka", "Marka düzenle". */
  noun: string
  /** The list's address, e.g. /api/brands. */
  path: string
  query: UseQueryResult<T[]>
  parent?: LookupParent<T>
}

/**
 * One kind of lookup on the definitions screens: the list with its state, adding, and the edit dialog (rename,
 * deactivate, reactivate). Lookups are never deleted; assets and history refer to them.
 */
export function LookupSection<T extends LookupItem>({ title, noun, path, query, parent }: LookupSectionProps<T>) {
  const [parentId, setParentId] = useState<number | null>(null)
  const [adding, setAdding] = useState<NewLookup | null>(null)
  const [editing, setEditing] = useState<T | null>(null)

  const items = (query.data ?? []).filter((item) => parent === undefined || parentId === null || parent.idOf(item) === parentId)
  const chosenParent = parent?.options.find((option) => option.id === parentId)
  const addDisabledReason = parent && !chosenParent?.isActive ? `Önce aktif bir ${parent.label.toLocaleLowerCase('tr-TR')} seçin` : null
  const headingId = `${path.replaceAll('/', '-')}-baslik`

  const add = () =>
    setAdding({
      noun,
      path,
      parent: parent && chosenParent ? { field: parent.field, id: chosenParent.id, name: chosenParent.name } : undefined,
    })

  return (
    <Card component="section" aria-labelledby={headingId}>
      <Box sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 2, p: 2 }}>
        <Typography id={headingId} variant="h6" component="h2" sx={{ flexGrow: 1 }}>
          {title}
          {query.data && (
            <Typography component="span" color="textSecondary" sx={{ ml: 1 }}>
              ({items.length})
            </Typography>
          )}
        </Typography>
        {parent && (
          <TextField
            select
            size="small"
            label={parent.label}
            value={parentId === null ? '' : String(parentId)}
            onChange={(event) => setParentId(event.target.value === '' ? null : Number(event.target.value))}
            sx={{ minWidth: 180 }}
          >
            <MenuItem value="">Tümü</MenuItem>
            {parent.options.map((option) => (
              <MenuItem key={option.id} value={String(option.id)}>
                {lookupLabel(option)}
              </MenuItem>
            ))}
          </TextField>
        )}
        <Tooltip title={addDisabledReason ?? ''}>
          <span>
            <Button variant="contained" startIcon={<AddIcon />} onClick={add} disabled={addDisabledReason !== null}>
              Yeni {noun}
            </Button>
          </span>
        </Tooltip>
      </Box>
      {query.isPending ? (
        <LoadingState message={`${title} yükleniyor...`} />
      ) : query.isError ? (
        <Box sx={{ p: 2 }}>
          <ErrorState
            title={`${title} yüklenemedi`}
            message="Liste alınamadı. Bağlantınızı kontrol edip tekrar deneyin."
            onRetry={() => void query.refetch()}
          />
        </Box>
      ) : items.length === 0 ? (
        <EmptyState
          title={`Henüz ${noun} yok`}
          description={parent && chosenParent ? `${chosenParent.name} ${parent.possessive} ${noun} tanımı yok.` : undefined}
        />
      ) : (
        <TableContainer>
          <Table size="small" aria-labelledby={headingId}>
            <TableHead>
              <TableRow>
                <TableCell>Ad</TableCell>
                {parent && <TableCell>{parent.label}</TableCell>}
                <TableCell>Durum</TableCell>
                <TableCell align="right">İşlemler</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {items.map((item) => (
                <TableRow key={item.id} hover>
                  <TableCell sx={{ color: item.isActive ? undefined : 'text.secondary' }}>{item.name}</TableCell>
                  {parent && <TableCell>{parent.nameOf(item)}</TableCell>}
                  <TableCell>
                    <Chip size="small" label={item.isActive ? 'Aktif' : 'Pasif'} color={item.isActive ? 'success' : 'default'} variant="outlined" />
                  </TableCell>
                  <TableCell align="right">
                    <Tooltip title="Düzenle">
                      <IconButton aria-label={`${item.name} düzenle`} onClick={() => setEditing(item)}>
                        <EditIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}
      <AddLookupDialog lookup={adding} onClose={() => setAdding(null)} onAdded={() => undefined} />
      {editing && (
        <EditLookupDialog
          key={`${editing.id}-${editing.rowVersion}`}
          noun={noun}
          path={path}
          item={editing}
          parentNote={parent ? `${parent.label}: ${parent.nameOf(editing)} (değiştirilemez)` : undefined}
          onClose={() => setEditing(null)}
        />
      )}
    </Card>
  )
}
