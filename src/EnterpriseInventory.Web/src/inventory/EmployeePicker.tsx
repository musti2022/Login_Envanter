import { Autocomplete, Box, TextField, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useState, type Ref } from 'react'
import { ApiError } from '../api/http'
import { employeeSearchLimits, employeeSearchQueryKey, searchEmployees, type EmployeeSearchItem } from './assignmentsApi'

/** Typing waits this long before the directory is asked, so a name is not searched letter by letter. */
export const employeeSearchDelayMs = 300

interface EmployeePickerProps {
  value: EmployeeSearchItem | null
  onChange: (value: EmployeeSearchItem | null) => void
  onBlur?: () => void
  inputRef?: Ref<HTMLInputElement>
  error?: string
  disabled?: boolean
}

/**
 * Searches Active Directory for the employee to give the asset to. Anyone with an enabled account can be chosen;
 * they need not be able to sign in to this application.
 */
export function EmployeePicker({ value, onChange, onBlur, inputRef, error, disabled }: EmployeePickerProps) {
  const [input, setInput] = useState('')
  const term = useDebouncedValue(input.trim(), employeeSearchDelayMs)
  const searchable = term.length >= employeeSearchLimits.minLength
  const search = useQuery({
    queryKey: employeeSearchQueryKey(term),
    queryFn: ({ signal }) => searchEmployees(term, signal),
    enabled: searchable,
    staleTime: 30_000,
    retry: false,
  })

  // The chosen person is shown by name in the box; that text is not a new search.
  const typing = value === null || input !== optionLabel(value)
  const waiting = typing && (input.trim() !== term || (searchable && search.isFetching))
  const options = typing && searchable && search.data ? search.data.items : value ? [value] : []

  return (
    <Autocomplete
      value={value}
      onChange={(_, next) => onChange(next)}
      inputValue={input}
      onInputChange={(_, next) => setInput(next)}
      options={options}
      filterOptions={(items) => items}
      getOptionLabel={optionLabel}
      isOptionEqualToValue={(option, selected) => option.objectGuid === selected.objectGuid}
      loading={waiting}
      loadingText="Aranıyor..."
      noOptionsText={noOptionsText(input.trim(), search.error)}
      disabled={disabled}
      onBlur={onBlur}
      renderOption={(props, option) => {
        const { key, ...rest } = props
        return (
          <Box component="li" key={key} {...rest}>
            <Box sx={{ minWidth: 0 }}>
              <Typography>{option.displayName}</Typography>
              <Typography variant="body2" color="textSecondary" sx={{ overflowWrap: 'anywhere' }}>
                {[option.userName, option.department, option.title].filter(Boolean).join(' · ')}
              </Typography>
            </Box>
          </Box>
        )
      }}
      renderInput={(params) => (
        <TextField
          {...params}
          inputRef={inputRef}
          label="Çalışan"
          required
          placeholder="Ad, soyad veya kullanıcı adı"
          error={Boolean(error) || search.isError}
          helperText={
            error ??
            (typing && searchable && search.data?.hasMore
              ? 'İlk sonuçlar gösteriliyor; aradığınız kişi yoksa adı daha ayrıntılı yazın.'
              : 'Active Directory\'de etkin hesabı olan herkes seçilebilir.')
          }
          slotProps={{ ...params.slotProps, htmlInput: { ...params.slotProps.htmlInput, maxLength: employeeSearchLimits.maxLength } }}
        />
      )}
    />
  )
}

function optionLabel(option: EmployeeSearchItem) {
  return `${option.displayName} (${option.userName})`
}

function noOptionsText(input: string, error: Error | null) {
  if (input.length < employeeSearchLimits.minLength) return `Aramak için en az ${employeeSearchLimits.minLength} harf yazın.`
  if (error instanceof ApiError && error.status === 503) return 'Çalışan dizinine şu anda ulaşılamıyor. Biraz sonra tekrar deneyin.'
  if (error instanceof ApiError && error.problem?.errors) return Object.values(error.problem.errors)[0]?.[0] ?? 'Arama geçersiz.'
  if (error) return 'Arama yapılamadı. Bağlantınızı kontrol edip tekrar deneyin.'
  return 'Bu adla etkin bir çalışan bulunamadı.'
}

function useDebouncedValue(value: string, delayMs: number) {
  const [settled, setSettled] = useState(value)
  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])
  return settled
}
