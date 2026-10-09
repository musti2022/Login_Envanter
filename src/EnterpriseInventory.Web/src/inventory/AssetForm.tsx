import { zodResolver } from '@hookform/resolvers/zod'
import AddIcon from '@mui/icons-material/Add'
import { Alert, AlertTitle, Box, Button, Card, CardContent, CircularProgress, IconButton, MenuItem, TextField, Tooltip, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { Controller, useForm, useWatch, type Control } from 'react-hook-form'
import { ApiError } from '../api/http'
import { AddLookupDialog, type NewLookup } from './AddLookupDialog'
import {
  assetFormSchema,
  assetLimits,
  emptyAssetForm,
  formFields,
  toBody,
  toFormInput,
  type AssetFormField,
  type AssetFormInput,
  type AssetFormValues,
} from './assetForm'
import type { AssetDetails, NamedReference, SaveAssetBody } from './assetsApi'
import { assetTypes, statusLabels, typeLabels, type AssetStatus } from './labels'
import { brandsQuery, citiesQuery, departmentsQuery, locationsQuery, lookupLabel, modelsQuery, type LookupItem } from './lookupsApi'

interface AssetFormProps {
  /** The asset as it was when editing began; left out when adding. */
  asset?: AssetDetails
  submitLabel: string
  /** Saves; an ApiError it throws is shown on the form. */
  onSubmit: (body: SaveAssetBody) => Promise<void>
  onCancel: () => void
  /** After a conflict: loads the asset as it is now and starts the edit over. */
  onReload?: () => void
}

interface Failure {
  title: string
  detail?: string
  correlationId?: string
  conflict?: boolean
}

interface Option {
  value: string
  label: string
}

/** Statuses a user may choose; "Zimmetli" is only reached by assigning the asset. */
const choosableStatuses: AssetStatus[] = ['Available', 'Faulty', 'Retired']

/**
 * The add and edit form. Lookups offer their active values; an edit also keeps the asset's current value even
 * when it has since been deactivated, because the API allows keeping it.
 */
export function AssetForm({ asset, submitLabel, onSubmit, onCancel, onReload }: AssetFormProps) {
  const {
    control,
    handleSubmit,
    setError,
    setValue,
    formState: { isSubmitting, isDirty },
  } = useForm<AssetFormInput, unknown, AssetFormValues>({
    resolver: zodResolver(assetFormSchema),
    defaultValues: asset ? toFormInput(asset) : emptyAssetForm,
  })
  const [failure, setFailure] = useState<Failure | null>(null)
  const [newLookup, setNewLookup] = useState<{ lookup: NewLookup; field: AssetFormField } | null>(null)

  const brandId = useWatch({ control, name: 'brandId' })
  const cityId = useWatch({ control, name: 'cityId' })
  const brands = useQuery(brandsQuery)
  const models = useQuery(modelsQuery(brandId ? Number(brandId) : null))
  const cities = useQuery(citiesQuery)
  const locations = useQuery(locationsQuery(cityId ? Number(cityId) : null))
  const departments = useQuery(departmentsQuery)

  const brandOptions = choices(brands.data, asset?.brand)
  const modelOptions = choices(models.data, asset && brandId === String(asset.brand.id) ? asset.model : undefined)
  const cityOptions = choices(cities.data, asset?.city)
  const locationOptions = choices(locations.data, asset?.location && cityId === String(asset.city.id) ? asset.location : undefined)
  const departmentOptions = choices(departments.data, asset?.department)
  const brandName = brandOptions.find((option) => option.value === brandId)?.label
  const cityName = cityOptions.find((option) => option.value === cityId)?.label

  const statusLocked = asset?.status === 'Assigned'
  const statusOptions = (statusLocked ? (['Assigned'] as AssetStatus[]) : choosableStatuses).map((status) => ({
    value: status,
    label: statusLabels[status],
  }))

  const submit = handleSubmit(async (values) => {
    setFailure(null)
    try {
      await onSubmit(toBody(values))
    } catch (error) {
      setFailure(showServerError(error, (field, message, focus) => setError(field, { message }, { shouldFocus: focus })))
    }
  })

  const added = (item: LookupItem) => {
    if (!newLookup) return
    setValue(newLookup.field, String(item.id), { shouldDirty: true, shouldValidate: true })
    if (newLookup.field === 'brandId') setValue('modelId', '', { shouldDirty: true })
    if (newLookup.field === 'cityId') setValue('locationId', '', { shouldDirty: true })
  }

  const addButton = (noun: string, path: string, field: AssetFormField, parent?: NewLookup['parent']) => (
    <Tooltip title={`Yeni ${noun} ekle`}>
      <span>
        <IconButton
          aria-label={`Yeni ${noun} ekle`}
          disabled={parent?.id === 0}
          onClick={() => setNewLookup({ lookup: { noun, path, parent }, field })}
          sx={{ mt: 0.5 }}
        >
          <AddIcon />
        </IconButton>
      </span>
    </Tooltip>
  )

  return (
    <Box component="form" noValidate onSubmit={submit} aria-label={asset ? 'Demirbaş düzenleme formu' : 'Demirbaş ekleme formu'}>
      {failure && (
        <Alert
          severity="error"
          role="alert"
          sx={{ mb: 2 }}
          action={
            failure.conflict && onReload ? (
              <Button color="inherit" size="small" onClick={onReload}>
                Güncel kaydı yükle
              </Button>
            ) : undefined
          }
        >
          <AlertTitle>{failure.title}</AlertTitle>
          {failure.detail}
          {failure.correlationId && <Box sx={{ mt: 0.5, fontSize: '0.8rem' }}>Hata kodu: {failure.correlationId}</Box>}
        </Alert>
      )}
      <Card>
        <CardContent sx={{ display: 'grid', gap: 3 }}>
          <Section title="Demirbaş bilgileri">
            <TextFieldController control={control} name="assetCode" label="Demirbaş Kodu" required maxLength={assetLimits.assetCode} />
            <SelectController
              control={control}
              name="assetType"
              label="Tür"
              required
              options={assetTypes.map((type) => ({ value: type, label: typeLabels[type] }))}
            />
            <SelectController
              control={control}
              name="status"
              label="Durum"
              required
              options={statusOptions}
              disabled={statusLocked}
              helperText={statusLocked ? 'Zimmetli demirbaşın durumu iade alınınca değişir.' : undefined}
            />
            <TextFieldController control={control} name="computerName" label="Bilgisayar Adı" maxLength={assetLimits.computerName} />
            <TextFieldController
              control={control}
              name="serialNumber"
              label="Seri No"
              maxLength={assetLimits.serialNumber}
              helperText="Boşluklar kaldırılır, harfler büyük harfle kaydedilir."
            />
          </Section>
          <Section title="Marka ve model">
            <SelectController
              control={control}
              name="brandId"
              label="Marka"
              required
              options={brandOptions}
              loadError={brands.isError}
              onValueChange={() => setValue('modelId', '')}
              action={addButton('marka', '/api/brands', 'brandId')}
            />
            <SelectController
              control={control}
              name="modelId"
              label="Model"
              required
              options={modelOptions}
              loadError={models.isError}
              disabled={!brandId}
              helperText={brandId ? undefined : 'Önce marka seçin.'}
              action={addButton('model', '/api/models', 'modelId', { field: 'brandId', id: Number(brandId || 0), name: brandName ?? '' })}
            />
          </Section>
          <Section title="Konum">
            <SelectController
              control={control}
              name="cityId"
              label="Şehir"
              required
              options={cityOptions}
              loadError={cities.isError}
              onValueChange={() => setValue('locationId', '')}
              action={addButton('şehir', '/api/cities', 'cityId')}
            />
            <SelectController
              control={control}
              name="locationId"
              label="Lokasyon"
              options={[{ value: '', label: 'Seçilmedi' }, ...locationOptions]}
              loadError={locations.isError}
              disabled={!cityId}
              helperText={cityId ? undefined : 'Önce şehir seçin.'}
              action={addButton('lokasyon', '/api/locations', 'locationId', { field: 'cityId', id: Number(cityId || 0), name: cityName ?? '' })}
            />
            <SelectController
              control={control}
              name="departmentId"
              label="Departman"
              required
              options={departmentOptions}
              loadError={departments.isError}
              action={addButton('departman', '/api/departments', 'departmentId')}
            />
          </Section>
          <Section title="Açıklama" columns={1}>
            <TextFieldController
              control={control}
              name="description"
              label="Açıklama"
              multiline
              maxLength={assetLimits.description}
            />
          </Section>
        </CardContent>
      </Card>
      <Box sx={{ display: 'flex', justifyContent: 'flex-end', flexWrap: 'wrap', gap: 1, mt: 2 }}>
        <Button onClick={onCancel}>Vazgeç</Button>
        <Button
          type="submit"
          variant="contained"
          disabled={isSubmitting || (asset !== undefined && !isDirty)}
          startIcon={isSubmitting ? <CircularProgress size={18} color="inherit" aria-hidden="true" /> : undefined}
        >
          {isSubmitting ? 'Kaydediliyor...' : submitLabel}
        </Button>
      </Box>
      <AddLookupDialog lookup={newLookup?.lookup ?? null} onClose={() => setNewLookup(null)} onAdded={added} />
    </Box>
  )
}

/** Active values, plus the asset's current one (also while the list is loading, so the select can show it). */
function choices(items: LookupItem[] | undefined, current: NamedReference | undefined): Option[] {
  const options = (items ?? [])
    .filter((item) => item.isActive || item.id === current?.id)
    .map((item) => ({ value: String(item.id), label: lookupLabel(item) }))
  if (current && !options.some((option) => option.value === String(current.id))) {
    options.unshift({ value: String(current.id), label: current.name })
  }
  return options
}

/**
 * What a refused save means for the form: field messages go under their fields (the first one gets the focus),
 * anything else is returned for the alert above the form.
 */
function showServerError(error: unknown, setFieldError: (field: AssetFormField, message: string, focus: boolean) => void): Failure | null {
  if (!(error instanceof ApiError)) {
    return { title: 'Kaydedilemedi', detail: 'Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.' }
  }

  const problem = error.problem
  if (error.code === 'concurrency_conflict') {
    return { title: problem?.title ?? 'Kayıt başka bir kullanıcı tarafından değiştirildi.', detail: problem?.detail, conflict: true }
  }

  const fieldErrors = Object.entries(problem?.errors ?? {})
  if (fieldErrors.length > 0) {
    const others: string[] = []
    let first = true
    for (const [key, messages] of fieldErrors) {
      if ((formFields as readonly string[]).includes(key)) {
        setFieldError(key as AssetFormField, messages[0], first)
        first = false
      } else {
        others.push(...messages)
      }
    }
    return others.length > 0 ? { title: problem?.title ?? 'Kaydedilemedi', detail: others.join(' ') } : null
  }

  return {
    title: problem?.title ?? 'Kaydedilemedi',
    detail: problem?.detail ?? 'Beklenmeyen bir hata oluştu. Tekrar deneyin.',
    correlationId: problem?.correlationId,
  }
}

function Section({ title, columns = 2, children }: { title: string; columns?: 1 | 2; children: ReactNode }) {
  return (
    <Box component="fieldset" sx={{ border: 0, p: 0, m: 0, minWidth: 0 }}>
      <Typography component="legend" variant="subtitle1" sx={{ fontWeight: 600, mb: 1.5 }}>
        {title}
      </Typography>
      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', md: columns === 2 ? 'repeat(2, minmax(0, 1fr))' : '1fr' } }}>
        {children}
      </Box>
    </Box>
  )
}

interface ControllerProps {
  control: Control<AssetFormInput>
  name: AssetFormField
  label: string
  required?: boolean
}

function TextFieldController({
  control,
  name,
  label,
  required,
  maxLength,
  multiline,
  helperText,
}: ControllerProps & { maxLength: number; multiline?: boolean; helperText?: string }) {
  return (
    <Controller
      control={control}
      name={name}
      render={({ field: { ref, ...field }, fieldState }) => (
        <TextField
          {...field}
          inputRef={ref}
          label={label}
          required={required}
          fullWidth
          multiline={multiline}
          minRows={multiline ? 3 : undefined}
          error={Boolean(fieldState.error)}
          helperText={fieldState.error?.message ?? helperText}
          slotProps={{ htmlInput: { maxLength } }}
        />
      )}
    />
  )
}

function SelectController({
  control,
  name,
  label,
  required,
  options,
  disabled,
  helperText,
  loadError,
  onValueChange,
  action,
}: ControllerProps & {
  options: Option[]
  disabled?: boolean
  helperText?: string
  loadError?: boolean
  onValueChange?: () => void
  action?: ReactNode
}) {
  return (
    <Controller
      control={control}
      name={name}
      render={({ field, fieldState }) => (
        <Box sx={{ display: 'flex', gap: 0.5, alignItems: 'flex-start' }}>
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
          {action}
        </Box>
      )}
    />
  )
}
