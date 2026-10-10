import CloseIcon from '@mui/icons-material/Close'
import FilterListIcon from '@mui/icons-material/FilterList'
import { Badge, Box, Button, Checkbox, Collapse, ListItemText, MenuItem, TextField, useMediaQuery, useTheme } from '@mui/material'
import { useEffect, useEffectEvent, useId, useState } from 'react'
import { actionLabel } from '../inventory/labels'
import { auditActions, auditEntityNames, type AuditAction, type AuditEntityName, type AuditLogParams } from './auditApi'
import { entityLabel } from './auditLabels'
import { activeAuditFilterCount, clearedAuditFilters } from './auditParams'

/** How long a text filter waits after the last key before asking the API. */
export const auditTypingDelayMs = 400

interface AuditFiltersProps {
  params: AuditLogParams
  onChange: (changes: Partial<AuditLogParams>) => void
}

/**
 * The audit screen's filters. Every change goes to the page address and back to page 1. On small screens the
 * filters fold under a button, so the records are in sight.
 */
export function AuditFilters({ params, onChange }: AuditFiltersProps) {
  const theme = useTheme()
  const isDesktop = useMediaQuery(theme.breakpoints.up('md'))
  const [open, setOpen] = useState(false)
  const panelId = useId()
  const set = (changes: Partial<AuditLogParams>) => onChange({ ...changes, page: 1 })
  const assetCodeApplies = params.entityName === null || params.entityName === 'Asset'
  const count = activeAuditFilterCount(params)

  return (
    <Box role="search" aria-label="Denetim kaydı filtreleri" sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}>
      {!isDesktop && (
        <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
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
          <Button onClick={() => set(clearedAuditFilters)} disabled={count === 0}>
            Filtreleri temizle
          </Button>
        </Box>
      )}
      <Collapse in={isDesktop || open} id={panelId} unmountOnExit>
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))', md: 'repeat(4, minmax(0, 1fr))' },
            gap: 2,
            pt: { xs: 2, md: 0 },
          }}
        >
          <TextField
            select
            size="small"
            label="Kayıt türü"
            value={params.entityName ?? ''}
            onChange={(event) => set({ entityName: (event.target.value || null) as AuditEntityName | null, entityId: null })}
            slotProps={{ inputLabel: { shrink: true }, select: { displayEmpty: true } }}
          >
            <MenuItem value="">Tümü</MenuItem>
            {auditEntityNames.map((name) => (
              <MenuItem key={name} value={name}>
                {entityLabel(name)}
              </MenuItem>
            ))}
          </TextField>
          <TextField
            select
            size="small"
            label="İşlem"
            value={params.action}
            onChange={(event) => {
              const value = event.target.value as unknown as AuditAction[] | string
              const chosen = typeof value === 'string' ? value.split(',') : value
              set({ action: auditActions.filter((action) => chosen.includes(action)) })
            }}
            slotProps={{
              inputLabel: { shrink: true },
              select: {
                multiple: true,
                displayEmpty: true,
                renderValue: (selected) => {
                  const chosen = selected as AuditAction[]
                  return chosen.length === 0 ? 'Tümü' : chosen.map(actionLabel).join(', ')
                },
              },
            }}
          >
            {auditActions.map((action) => (
              <MenuItem key={action} value={action} dense>
                <Checkbox checked={params.action.includes(action)} size="small" tabIndex={-1} disableRipple sx={{ py: 0 }} />
                <ListItemText primary={actionLabel(action)} />
              </MenuItem>
            ))}
          </TextField>
          <TypedFilter
            label="Kullanıcı"
            value={params.userName}
            maxLength={100}
            placeholder="ör. ayse"
            onApply={(userName) => set({ userName })}
          />
          <TypedFilter
            label="Demirbaş kodu"
            value={params.assetCode}
            maxLength={50}
            placeholder="Kodun bir kısmı"
            disabledText={assetCodeApplies ? undefined : 'Yalnızca demirbaş kayıtlarında'}
            onApply={(assetCode) => set({ assetCode })}
          />
          <TextField
            type="date"
            size="small"
            label="Başlangıç tarihi"
            value={params.fromDate}
            onChange={(event) => set({ fromDate: event.target.value })}
            slotProps={{ inputLabel: { shrink: true }, htmlInput: { max: params.toDate || undefined } }}
          />
          <TextField
            type="date"
            size="small"
            label="Bitiş tarihi"
            value={params.toDate}
            onChange={(event) => set({ toDate: event.target.value })}
            helperText="Seçilen gün dahil"
            slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: params.fromDate || undefined } }}
          />
          <TypedFilter
            label="İşlem numarası"
            value={params.correlationId}
            maxLength={64}
            placeholder="Hata mesajındaki numara"
            onApply={(correlationId) => set({ correlationId })}
          />
          <Box sx={{ display: 'flex', alignItems: 'flex-start', gap: 1, flexWrap: 'wrap' }}>
            {params.entityName && params.entityId && (
              <Button
                variant="outlined"
                size="small"
                endIcon={<CloseIcon />}
                aria-label={`${entityLabel(params.entityName)} no: ${params.entityId} filtresini kaldır`}
                onClick={() => set({ entityId: null })}
              >
                {entityLabel(params.entityName)} no: {params.entityId}
              </Button>
            )}
            {isDesktop && (
              <Button onClick={() => set(clearedAuditFilters)} disabled={count === 0}>
                Filtreleri temizle
              </Button>
            )}
          </Box>
        </Box>
      </Collapse>
    </Box>
  )
}

interface TypedFilterProps {
  label: string
  value: string
  maxLength: number
  placeholder?: string
  /** Set when the filter does not apply to the current choice; the field is disabled and says why. */
  disabledText?: string
  onApply: (value: string) => void
}

/** A text filter. Typing waits a moment before filtering; Enter filters at once. */
function TypedFilter({ label, value, maxLength, placeholder, disabledText, onApply }: TypedFilterProps) {
  const [text, setText] = useState(value)
  const [shown, setShown] = useState(value)

  // The address changed from outside (filters cleared, back button): show what it says.
  if (value !== shown) {
    setShown(value)
    setText(value)
  }

  const apply = useEffectEvent((text: string) => onApply(text))
  useEffect(() => {
    if (text.trim() === value.trim()) return
    const timer = setTimeout(() => apply(text), auditTypingDelayMs)
    return () => clearTimeout(timer)
  }, [text, value])

  return (
    <TextField
      size="small"
      label={label}
      placeholder={placeholder}
      value={disabledText ? '' : text}
      disabled={disabledText !== undefined}
      helperText={disabledText}
      onChange={(event) => setText(event.target.value)}
      onKeyDown={(event) => {
        if (event.key === 'Enter' && text.trim() !== value.trim()) onApply(text)
      }}
      slotProps={{ inputLabel: { shrink: true }, htmlInput: { maxLength } }}
    />
  )
}
