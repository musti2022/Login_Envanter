import FilterListIcon from '@mui/icons-material/FilterList'
import { Badge, Box, Button, Collapse, MenuItem, TextField, useMediaQuery, useTheme } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useEffectEvent, useId, useState } from 'react'
import { TypedFilter } from '../audit/AuditFilters'
import { LookupSelect, MultiSelect } from '../inventory/AssetFilters'
import { assetTypes, typeLabels } from '../inventory/labels'
import { citiesQuery, departmentsQuery, type LookupItem } from '../inventory/lookupsApi'
import { activeMovementFilterCount, clearedMovementFilters, monthPeriod } from './reportParams'
import { movementKinds, movementLabels, type MovementKind, type MovementParams } from './reportsApi'

interface MovementFiltersProps {
  params: MovementParams
  onChange: (changes: Partial<MovementParams>, options?: { replace?: boolean }) => void
}

/**
 * The movement report's period and filters. Every change goes to the page address and back to page 1. On small
 * screens the filters fold under a button, so the movements are in sight.
 */
export function MovementFilters({ params, onChange }: MovementFiltersProps) {
  const theme = useTheme()
  const isDesktop = useMediaQuery(theme.breakpoints.up('md'))
  const [open, setOpen] = useState(false)
  const panelId = useId()
  const cities = useQuery(citiesQuery)
  const departments = useQuery(departmentsQuery)

  // A city or department the lists do not have (a stale link) would filter by something the user cannot see.
  const dropUnknown = useEffectEvent((changes: Partial<MovementParams>) => onChange({ ...changes, page: 1 }, { replace: true }))
  useEffect(() => {
    const unknown: Partial<MovementParams> = {}
    if (isUnknown(params.cityId, cities.data)) unknown.cityId = null
    if (isUnknown(params.departmentId, departments.data)) unknown.departmentId = null
    if (Object.keys(unknown).length > 0) dropUnknown(unknown)
  }, [params.cityId, params.departmentId, cities.data, departments.data])

  const set = (changes: Partial<MovementParams>) => onChange({ ...changes, page: 1 })
  const count = activeMovementFilterCount(params)

  return (
    <Box role="search" aria-label="Zimmet hareketi filtreleri" sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}>
      <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
        {!isDesktop && (
          <Button
            variant="outlined"
            aria-expanded={open}
            aria-controls={panelId}
            aria-label={count > 0 ? `Filtreler, ${count} filtre açık` : 'Filtreler'}
            onClick={() => setOpen((value) => !value)}
            startIcon={
              <Badge badgeContent={count} color="primary">
                <FilterListIcon />
              </Badge>
            }
          >
            Filtreler
          </Button>
        )}
        <Button variant="outlined" size="small" onClick={() => set(monthPeriod(new Date(), 0))}>
          Bu ay
        </Button>
        <Button variant="outlined" size="small" onClick={() => set(monthPeriod(new Date(), 1))}>
          Geçen ay
        </Button>
        <Button onClick={() => set(clearedMovementFilters)} disabled={count === 0}>
          Filtreleri temizle
        </Button>
      </Box>
      <Collapse in={isDesktop || open} id={panelId} unmountOnExit>
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))', md: 'repeat(4, minmax(0, 1fr))' },
            gap: 2,
            pt: 2,
          }}
        >
          <TextField
            type="date"
            size="small"
            label="Başlangıç tarihi"
            value={params.from}
            onChange={(event) => set({ from: event.target.value })}
            slotProps={{ inputLabel: { shrink: true }, htmlInput: { max: params.to || undefined } }}
          />
          <TextField
            type="date"
            size="small"
            label="Bitiş tarihi"
            value={params.to}
            onChange={(event) => set({ to: event.target.value })}
            helperText="Seçilen gün dahil"
            slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: params.from || undefined } }}
          />
          <TextField
            select
            size="small"
            label="Hareket"
            value={params.movement ?? ''}
            onChange={(event) => set({ movement: (event.target.value || null) as MovementKind | null })}
            slotProps={{ inputLabel: { shrink: true }, select: { displayEmpty: true } }}
          >
            <MenuItem value="">Tümü</MenuItem>
            {movementKinds.map((kind) => (
              <MenuItem key={kind} value={kind}>
                {movementLabels[kind]}
              </MenuItem>
            ))}
          </TextField>
          <TypedFilter
            label="Ara"
            value={params.search}
            maxLength={100}
            placeholder="Demirbaş kodu veya kullanıcı"
            onApply={(search) => set({ search })}
          />
          <MultiSelect
            label="Tür"
            values={params.assetType}
            options={assetTypes}
            labels={typeLabels}
            onChange={(assetType) => set({ assetType })}
          />
          <LookupSelect label="Şehir" value={params.cityId} query={cities} onChange={(cityId) => set({ cityId })} />
          <LookupSelect
            label="Departman"
            value={params.departmentId}
            query={departments}
            onChange={(departmentId) => set({ departmentId })}
          />
        </Box>
      </Collapse>
    </Box>
  )
}

function isUnknown(value: number | null, items: LookupItem[] | undefined) {
  return value !== null && items !== undefined && !items.some((item) => item.id === value)
}
