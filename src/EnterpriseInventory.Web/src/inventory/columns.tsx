import { Box, Link, Typography } from '@mui/material'
import type { ReactNode } from 'react'
import { Link as RouterLink } from 'react-router'
import type { AssetListItem, SortField } from './assetsApi'
import { formatDateTime, typeLabels } from './labels'
import { StatusChip } from './StatusChip'

export interface AssetColumn {
  key: string
  label: string
  /** The API's sortBy for this column; columns without one cannot be sorted. */
  sortBy?: SortField
  /** Shown until the user hides it; the rest can be turned on from the column menu. */
  visibleByDefault: boolean
  render: (asset: AssetListItem) => ReactNode
}

const empty = (
  <Typography component="span" color="textDisabled" aria-label="Boş">
    —
  </Typography>
)

const text = (value: string | null) => value ?? empty

/** The inventory table's columns, in the order the users asked for (proje_talimatlari.md §4). */
export const assetColumns: readonly AssetColumn[] = [
  {
    key: 'assetCode',
    label: 'Demirbaş Kodu',
    sortBy: 'assetCode',
    visibleByDefault: true,
    render: (asset) => (
      <Link component={RouterLink} to={`/envanter/${asset.id}`} sx={{ fontWeight: 600, whiteSpace: 'nowrap' }}>
        {asset.assetCode}
      </Link>
    ),
  },
  {
    key: 'assignedUser',
    label: 'Kullanıcı Adı',
    sortBy: 'assignedDisplayName',
    visibleByDefault: true,
    render: (asset) =>
      asset.assignedUserName ? (
        <Box>
          <Typography variant="body2">{asset.assignedDisplayName}</Typography>
          <Typography variant="caption" color="textSecondary">
            {asset.assignedUserName}
          </Typography>
        </Box>
      ) : (
        empty
      ),
  },
  { key: 'computerName', label: 'Bilgisayar Adı', sortBy: 'computerName', visibleByDefault: true, render: (a) => text(a.computerName) },
  { key: 'brandName', label: 'Marka', sortBy: 'brandName', visibleByDefault: true, render: (a) => a.brandName },
  { key: 'modelName', label: 'Model', sortBy: 'modelName', visibleByDefault: true, render: (a) => a.modelName },
  { key: 'serialNumber', label: 'Seri No', sortBy: 'serialNumber', visibleByDefault: true, render: (a) => text(a.serialNumber) },
  {
    key: 'assignmentDescription',
    label: 'Zimmet Tanımı',
    visibleByDefault: true,
    render: (a) => text(a.assignmentDescription),
  },
  {
    key: 'city',
    label: 'Lokasyon/Şehir',
    sortBy: 'cityName',
    visibleByDefault: true,
    render: (asset) => (
      <Box>
        <Typography variant="body2">{asset.cityName}</Typography>
        {asset.locationName && (
          <Typography variant="caption" color="textSecondary">
            {asset.locationName}
          </Typography>
        )}
      </Box>
    ),
  },
  { key: 'departmentName', label: 'Lokasyon/Departman', sortBy: 'departmentName', visibleByDefault: true, render: (a) => a.departmentName },
  { key: 'assetType', label: 'Tür', sortBy: 'assetType', visibleByDefault: false, render: (a) => typeLabels[a.assetType] },
  { key: 'status', label: 'Durum', sortBy: 'status', visibleByDefault: true, render: (a) => <StatusChip status={a.status} /> },
  {
    key: 'updatedAt',
    label: 'Son Değişiklik',
    sortBy: 'updatedAt',
    visibleByDefault: false,
    render: (a) => formatDateTime(a.updatedAt ?? a.createdAt),
  },
]

export const defaultHiddenColumns: ReadonlySet<string> = new Set(assetColumns.filter((c) => !c.visibleByDefault).map((c) => c.key))
