import FilterListIcon from '@mui/icons-material/FilterList'
import SearchIcon from '@mui/icons-material/Search'
import {
  Badge,
  Box,
  Button,
  Checkbox,
  Collapse,
  FormControlLabel,
  InputAdornment,
  ListItemText,
  MenuItem,
  Switch,
  TextField,
  useMediaQuery,
  useTheme,
} from '@mui/material'
import { useQuery, type UseQueryResult } from '@tanstack/react-query'
import { useEffect, useEffectEvent, useId, useState } from 'react'
import type { AssetListParams } from './assetsApi'
import { assetStatuses, assetTypes, statusLabels, typeLabels } from './labels'
import { activeFilterCount, clearedFilters } from './listParams'
import {
  brandsQuery,
  citiesQuery,
  departmentsQuery,
  locationsQuery,
  lookupLabel,
  modelsQuery,
  type LookupItem,
} from './lookupsApi'

/** How long the search waits after the last key before asking the API. */
export const searchDelayMs = 400

interface AssetFiltersProps {
  params: AssetListParams
  /** Applies the changes to the list address; `replace` corrects it without a new history entry. */
  onChange: (changes: Partial<AssetListParams>, options?: { replace?: boolean }) => void
}

/**
 * Search and filters above the inventory table. Every change goes to the page address and back to page 1, so
 * the table, the address and the API request always agree. On small screens the filters fold under a button.
 */
export function AssetFilters({ params, onChange }: AssetFiltersProps) {
  const theme = useTheme()
  const isDesktop = useMediaQuery(theme.breakpoints.up('md'))
  const [open, setOpen] = useState(false)
  const panelId = useId()

  const brands = useQuery(brandsQuery)
  const models = useQuery(modelsQuery(params.brandId))
  const cities = useQuery(citiesQuery)
  const locations = useQuery(locationsQuery(params.cityId))
  const departments = useQuery(departmentsQuery)

  // A value the lists do not have (a hand-edited or stale link, a model of another brand) would filter by
  // something the user cannot see; it is dropped once the list is known.
  const dropUnknown = useEffectEvent((changes: Partial<AssetListParams>) => onChange({ ...changes, page: 1 }, { replace: true }))
  useEffect(() => {
    const unknown: Partial<AssetListParams> = {}
    if (isUnknown(params.brandId, brands.data)) Object.assign(unknown, { brandId: null, modelId: null })
    if (isUnknown(params.modelId, models.data)) unknown.modelId = null
    if (isUnknown(params.cityId, cities.data)) Object.assign(unknown, { cityId: null, locationId: null })
    if (isUnknown(params.locationId, locations.data)) unknown.locationId = null
    if (isUnknown(params.departmentId, departments.data)) unknown.departmentId = null
    if (Object.keys(unknown).length > 0) dropUnknown(unknown)
  }, [params.brandId, params.modelId, params.cityId, params.locationId, params.departmentId, brands.data, models.data, cities.data, locations.data, departments.data])

  const set = (changes: Partial<AssetListParams>) => onChange({ ...changes, page: 1 })
  const total = activeFilterCount(params)
  const panelCount = total - (params.search.trim() ? 1 : 0)
  const showPanel = isDesktop || open

  return (
    <Box role="search" aria-label="Demirbaş arama ve filtreleri" sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}>
      <Box sx={{ display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
        <SearchField value={params.search} onSearch={(search) => set({ search })} />
        {!isDesktop && (
          <Button
            variant="outlined"
            aria-expanded={open}
            aria-controls={panelId}
            aria-label={panelCount > 0 ? `Filtreler, ${panelCount} filtre açık` : 'Filtreler'}
            onClick={() => setOpen((value) => !value)}
            startIcon={
              <Badge badgeContent={panelCount} color="primary">
                <FilterListIcon />
              </Badge>
            }
          >
            Filtreler
          </Button>
        )}
        <Button onClick={() => set(clearedFilters)} disabled={total === 0}>
          Filtreleri temizle
        </Button>
      </Box>
      <Collapse in={showPanel} id={panelId} unmountOnExit>
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))', md: 'repeat(4, minmax(0, 1fr))' },
            gap: 2,
            pt: 2,
          }}
        >
          <MultiSelect
            label="Durum"
            values={params.status}
            options={assetStatuses}
            labels={statusLabels}
            onChange={(status) => set({ status })}
          />
          <MultiSelect
            label="Tür"
            values={params.assetType}
            options={assetTypes}
            labels={typeLabels}
            onChange={(assetType) => set({ assetType })}
          />
          <LookupSelect label="Marka" value={params.brandId} query={brands} onChange={(brandId) => set({ brandId, modelId: null })} />
          <LookupSelect
            label="Model"
            value={params.modelId}
            query={models}
            disabledText={params.brandId === null ? 'Önce marka seçin' : undefined}
            onChange={(modelId) => set({ modelId })}
          />
          <LookupSelect label="Şehir" value={params.cityId} query={cities} onChange={(cityId) => set({ cityId, locationId: null })} />
          <LookupSelect
            label="Lokasyon"
            value={params.locationId}
            query={locations}
            disabledText={params.cityId === null ? 'Önce şehir seçin' : undefined}
            onChange={(locationId) => set({ locationId })}
          />
          <LookupSelect
            label="Departman"
            value={params.departmentId}
            query={departments}
            onChange={(departmentId) => set({ departmentId })}
          />
          <FormControlLabel
            control={<Switch checked={params.archived} onChange={(event) => set({ archived: event.target.checked })} />}
            label="Arşivlenmişleri göster"
          />
        </Box>
      </Collapse>
    </Box>
  )
}

function isUnknown(value: number | null, items: LookupItem[] | undefined) {
  return value !== null && items !== undefined && !items.some((item) => item.id === value)
}

/** The search box. Typing waits a moment before searching; Enter searches at once. */
function SearchField({ value, onSearch }: { value: string; onSearch: (search: string) => void }) {
  const [text, setText] = useState(value)
  const [shown, setShown] = useState(value)

  // The address changed from outside (filters cleared, back button): show what it says.
  if (value !== shown) {
    setShown(value)
    setText(value)
  }

  const search = useEffectEvent((text: string) => onSearch(text))
  useEffect(() => {
    if (text.trim() === value.trim()) return
    const timer = setTimeout(() => search(text), searchDelayMs)
    return () => clearTimeout(timer)
  }, [text, value])

  return (
    <TextField
      type="search"
      size="small"
      label="Ara"
      placeholder="Kod, seri no, kullanıcı, marka..."
      value={text}
      onChange={(event) => setText(event.target.value)}
      onKeyDown={(event) => {
        if (event.key === 'Enter' && text.trim() !== value.trim()) onSearch(text)
      }}
      sx={{ flex: '1 1 260px', maxWidth: { md: 420 } }}
      slotProps={{
        htmlInput: { maxLength: 100 },
        input: {
          startAdornment: (
            <InputAdornment position="start">
              <SearchIcon fontSize="small" />
            </InputAdornment>
          ),
        },
      }}
    />
  )
}

interface MultiSelectProps<T extends string> {
  label: string
  values: T[]
  options: readonly T[]
  labels: Record<T, string>
  onChange: (values: T[]) => void
}

function MultiSelect<T extends string>({ label, values, options, labels, onChange }: MultiSelectProps<T>) {
  return (
    <TextField
      select
      size="small"
      label={label}
      value={values}
      onChange={(event) => {
        const value = event.target.value as unknown as T[] | string
        // Order the choices as the list does, so the address does not depend on the order of the clicks.
        const chosen = typeof value === 'string' ? value.split(',') : value
        onChange(options.filter((option) => chosen.includes(option)))
      }}
      slotProps={{
        inputLabel: { shrink: true },
        select: {
          multiple: true,
          displayEmpty: true,
          renderValue: (selected) => {
            const chosen = selected as T[]
            return chosen.length === 0 ? 'Tümü' : chosen.map((value) => labels[value]).join(', ')
          },
        },
      }}
    >
      {options.map((option) => (
        <MenuItem key={option} value={option} dense>
          <Checkbox checked={values.includes(option)} size="small" tabIndex={-1} disableRipple sx={{ py: 0 }} />
          <ListItemText primary={labels[option]} />
        </MenuItem>
      ))}
    </TextField>
  )
}

interface LookupSelectProps {
  label: string
  value: number | null
  query: UseQueryResult<LookupItem[]>
  /** Set when the list depends on another choice that is not made yet; the select is disabled and says why. */
  disabledText?: string
  onChange: (value: number | null) => void
}

function LookupSelect({ label, value, query, disabledText, onChange }: LookupSelectProps) {
  const items = query.data ?? []
  const known = value !== null && items.some((item) => item.id === value)
  const helperText = disabledText ?? (query.isError ? 'Liste alınamadı.' : undefined)

  return (
    <TextField
      select
      size="small"
      label={label}
      value={known ? String(value) : ''}
      disabled={disabledText !== undefined}
      error={query.isError}
      helperText={helperText}
      onChange={(event) => onChange(event.target.value === '' ? null : Number(event.target.value))}
      slotProps={{ inputLabel: { shrink: true }, select: { displayEmpty: true } }}
    >
      <MenuItem value="">Tümü</MenuItem>
      {items.map((item) => (
        <MenuItem key={item.id} value={String(item.id)}>
          {lookupLabel(item)}
        </MenuItem>
      ))}
    </TextField>
  )
}
