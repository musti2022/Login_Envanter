import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, AlertTitle, Box, Button, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, TextField } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { Controller, useForm, useWatch, type Control } from 'react-hook-form'
import { z } from 'zod'
import { ApiError } from '../api/http'
import { moveAsset, type AssetDetails } from './assetsApi'
import { citiesQuery, departmentsQuery, locationsQuery, lookupChoices, type LookupOption } from './lookupsApi'

const schema = z.object({
  cityId: z.string().min(1, 'Şehir seçilmelidir.'),
  locationId: z.string(),
  departmentId: z.string().min(1, 'Departman seçilmelidir.'),
})

type FormValues = z.infer<typeof schema>
type Field = keyof FormValues

const fields: readonly Field[] = ['cityId', 'locationId', 'departmentId']

function valuesOf(asset: AssetDetails | null): FormValues {
  return {
    cityId: asset ? String(asset.city.id) : '',
    locationId: asset?.location ? String(asset.location.id) : '',
    departmentId: asset ? String(asset.department.id) : '',
  }
}

interface Failure {
  title: string
  detail?: string
  correlationId?: string
  conflict?: boolean
}

interface MoveAssetDialogProps {
  /** The asset as shown when the user asked to move it; its row version goes with the request. */
  asset: AssetDetails | null
  onClose: () => void
  onMoved: (asset: AssetDetails) => void
  /** The asset changed meanwhile: the page shows it again so the user can decide on what it is now. */
  onConflict: () => void
}

/**
 * "Konum Değiştir": moves the asset to another city, location or department without a full edit. The lists offer
 * active values; the asset's current ones stay choosable even when they have since been deactivated.
 */
export function MoveAssetDialog({ asset, onClose, onMoved, onConflict }: MoveAssetDialogProps) {
  const {
    control,
    handleSubmit,
    reset,
    setError,
    setValue,
    formState: { isSubmitting, isDirty },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: valuesOf(asset) })
  const [failure, setFailure] = useState<Failure | null>(null)

  // Each opening starts from where the asset is now.
  useEffect(() => {
    if (asset) reset(valuesOf(asset))
  }, [asset, reset])

  const cityId = useWatch({ control, name: 'cityId' })
  const cities = useQuery({ ...citiesQuery, enabled: asset !== null })
  const departments = useQuery({ ...departmentsQuery, enabled: asset !== null })
  const locations = useQuery({ ...locationsQuery(cityId ? Number(cityId) : null), enabled: asset !== null && Boolean(cityId) })

  const cityOptions = lookupChoices(cities.data, asset?.city)
  const locationOptions = lookupChoices(locations.data, asset?.location && cityId === String(asset.city.id) ? asset.location : undefined)
  const departmentOptions = lookupChoices(departments.data, asset?.department)

  const close = () => {
    setFailure(null)
    onClose()
  }

  const submit = handleSubmit(async (values) => {
    if (!asset) return
    setFailure(null)
    try {
      const moved = await moveAsset(
        asset.id,
        {
          cityId: Number(values.cityId),
          departmentId: Number(values.departmentId),
          locationId: values.locationId === '' ? null : Number(values.locationId),
        },
        asset.rowVersion,
      )
      onMoved(moved)
    } catch (error) {
      const shown = showServerError(error, (field, message, focus) => setError(field, { message }, { shouldFocus: focus }))
      setFailure(shown)
      if (shown?.conflict) onConflict()
    }
  })

  return (
    <Dialog open={asset !== null} onClose={isSubmitting ? undefined : close} fullWidth maxWidth="sm">
      <Box component="form" noValidate onSubmit={submit} aria-label="Konum değiştirme formu">
        <DialogTitle>Konum değiştir</DialogTitle>
        <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          {failure && (
            <Alert severity="error" role="alert">
              <AlertTitle>{failure.title}</AlertTitle>
              {failure.detail}
              {failure.correlationId && <Box sx={{ mt: 0.5, fontSize: '0.8rem' }}>Hata kodu: {failure.correlationId}</Box>}
            </Alert>
          )}
          <SelectField
            control={control}
            name="cityId"
            label="Şehir"
            required
            options={cityOptions}
            loadError={cities.isError}
            disabled={isSubmitting}
            onValueChange={() => setValue('locationId', '', { shouldDirty: true })}
          />
          <SelectField
            control={control}
            name="locationId"
            label="Lokasyon"
            options={[{ value: '', label: 'Seçilmedi' }, ...locationOptions]}
            loadError={locations.isError}
            disabled={isSubmitting || !cityId}
            helperText={cityId ? undefined : 'Önce şehir seçin.'}
          />
          <SelectField
            control={control}
            name="departmentId"
            label="Departman"
            required
            options={departmentOptions}
            loadError={departments.isError}
            disabled={isSubmitting}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={close} disabled={isSubmitting}>
            Vazgeç
          </Button>
          <Button
            type="submit"
            variant="contained"
            // Nothing chosen differs from where the asset is; after a conflict the user first looks at it again.
            disabled={isSubmitting || !isDirty || failure?.conflict === true}
            startIcon={isSubmitting ? <CircularProgress size={18} color="inherit" aria-hidden="true" /> : undefined}
          >
            {isSubmitting ? 'Kaydediliyor...' : 'Konumu Kaydet'}
          </Button>
        </DialogActions>
      </Box>
    </Dialog>
  )
}

function SelectField({
  control,
  name,
  label,
  required,
  options,
  disabled,
  helperText,
  loadError,
  onValueChange,
}: {
  control: Control<FormValues>
  name: Field
  label: string
  required?: boolean
  options: LookupOption[]
  disabled?: boolean
  helperText?: string
  loadError?: boolean
  onValueChange?: () => void
}) {
  return (
    <Controller
      control={control}
      name={name}
      render={({ field, fieldState }) => (
        <TextField
          select
          name={field.name}
          value={options.some((option) => option.value === field.value) ? field.value : ''}
          onChange={(event) => {
            field.onChange(event.target.value)
            onValueChange?.()
          }}
          onBlur={field.onBlur}
          inputRef={field.ref}
          label={label}
          required={required}
          fullWidth
          disabled={disabled}
          error={Boolean(fieldState.error) || loadError}
          helperText={fieldState.error?.message ?? (loadError ? 'Liste alınamadı.' : helperText)}
        >
          {options.map((option) => (
            <MenuItem key={option.value} value={option.value}>
              {option.label}
            </MenuItem>
          ))}
        </TextField>
      )}
    />
  )
}

/**
 * What a refused move means for the form: field messages go under their fields (the first one gets the focus),
 * anything else is returned for the alert above them.
 */
function showServerError(error: unknown, setFieldError: (field: Field, message: string, focus: boolean) => void): Failure | null {
  if (!(error instanceof ApiError)) {
    return { title: 'Konum değiştirilemedi', detail: 'Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.' }
  }

  const problem = error.problem
  if (error.code === 'concurrency_conflict') {
    return {
      title: 'Kayıt siz bakarken değiştirildi.',
      detail: 'Konum değiştirilmedi. Sayfadaki bilgiler yenilendi; kontrol edip tekrar deneyin.',
      conflict: true,
    }
  }

  const fieldErrors = Object.entries(problem?.errors ?? {})
  if (fieldErrors.length > 0) {
    const others: string[] = []
    let first = true
    for (const [key, messages] of fieldErrors) {
      if ((fields as readonly string[]).includes(key)) {
        setFieldError(key as Field, messages[0], first)
        first = false
      } else {
        others.push(...messages)
      }
    }
    return others.length > 0 ? { title: problem?.title ?? 'Konum değiştirilemedi', detail: others.join(' ') } : null
  }

  return {
    title: problem?.title ?? 'Konum değiştirilemedi',
    detail: problem?.detail ?? 'Beklenmeyen bir hata oluştu. Tekrar deneyin.',
    correlationId: error.status >= 500 ? problem?.correlationId : undefined,
    conflict: error.code === 'asset_archived' || error.code === 'asset_not_found',
  }
}
