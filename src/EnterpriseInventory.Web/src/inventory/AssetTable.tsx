import EditIcon from '@mui/icons-material/Edit'
import VisibilityIcon from '@mui/icons-material/Visibility'
import {
  IconButton,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TablePagination,
  TableRow,
  TableSortLabel,
  Tooltip,
} from '@mui/material'
import { Link as RouterLink } from 'react-router'
import type { AssetListItem, AssetListParams, PagedResult, SortField } from './assetsApi'
import { assetColumns } from './columns'
import { pageSizes } from './listParams'

interface AssetTableProps {
  page: PagedResult<AssetListItem>
  params: AssetListParams
  hiddenColumns: ReadonlySet<string>
  onSort: (sortBy: SortField) => void
  onPage: (page: number) => void
  onPageSize: (pageSize: number) => void
}

/**
 * The inventory table. Sorting and paging happen on the server: the table only shows the page it is given
 * and reports what the user asked for.
 */
export function AssetTable({ page, params, hiddenColumns, onSort, onPage, onPageSize }: AssetTableProps) {
  const columns = assetColumns.filter((column) => !hiddenColumns.has(column.key))

  return (
    <>
      <TableContainer>
        <Table size="small" aria-label="Demirbaş listesi" sx={{ minWidth: 960, '& td, & th': { whiteSpace: 'nowrap' } }}>
          <TableHead>
            <TableRow>
              {columns.map((column) => {
                const sorted = column.sortBy !== undefined && params.sortBy === column.sortBy
                return (
                  <TableCell key={column.key} sortDirection={sorted ? params.sortDirection : false} sx={{ fontWeight: 600 }}>
                    {column.sortBy ? (
                      <TableSortLabel
                        active={sorted}
                        direction={sorted ? params.sortDirection : 'asc'}
                        onClick={() => onSort(column.sortBy!)}
                      >
                        {column.label}
                      </TableSortLabel>
                    ) : (
                      column.label
                    )}
                  </TableCell>
                )
              })}
              <TableCell align="right" sx={{ fontWeight: 600 }}>
                İşlemler
              </TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {page.items.map((asset) => (
              <TableRow key={asset.id} hover>
                {columns.map((column) => (
                  <TableCell key={column.key}>{column.render(asset)}</TableCell>
                ))}
                <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                  <Tooltip title="Detay">
                    <IconButton
                      size="small"
                      component={RouterLink}
                      to={`/envanter/${asset.id}`}
                      aria-label={`${asset.assetCode} detayı`}
                    >
                      <VisibilityIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                  {!asset.isArchived && (
                    <Tooltip title="Düzenle">
                      <IconButton
                        size="small"
                        component={RouterLink}
                        to={`/envanter/${asset.id}/duzenle`}
                        aria-label={`${asset.assetCode} düzenle`}
                      >
                        <EditIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  )}
                </TableCell>
              </TableRow>
            ))}
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
      />
    </>
  )
}
