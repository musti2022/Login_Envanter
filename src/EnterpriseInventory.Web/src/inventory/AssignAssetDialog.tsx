import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, AlertTitle, Box, Button, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material'
import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { z } from 'zod'
import { ApiError } from '../api/http'
import { assignAsset, assignmentLimits, type EmployeeSearchItem } from './assignmentsApi'
import type { AssetDetails } from './assetsApi'
import { EmployeePicker } from './EmployeePicker'

const schema = z.object({
  employee: z.custom<EmployeeSearchItem | null>().refine((value) => value !== null, 'Zimmetlenecek çalışanı seçin.'),
  assignmentDescription: z
    .string()
    .trim()
    .min(1, 'Zimmet tanımı zorunludur.')
    .max(assignmentLimits.assignmentDescription, `Zimmet tanımı en fazla ${assignmentLimits.assignmentDescription} karakter olabilir.`),
  notes: z.string().trim().max(assignmentLimits.notes, `Not en fazla ${assignmentLimits.notes} karakter olabilir.`),
})

type FormInput = z.input<typeof schema>
type FormValues = z.output<typeof schema>

const emptyForm: FormInput = { employee: null, assignmentDescription: '', notes: '' }

/** API field names and the form fields they belong to. */
const apiFields: Record<string, keyof FormInput> = {
  employeeObjectGuid: 'employee',
  assignmentDescription: 'assignmentDescription',
  notes: 'notes',
}

interface Failure {
  title: string
  detail?: string
  correlationId?: string
  conflict?: boolean
}

interface AssignAssetDialogProps {
  /** The asset as shown when the user asked to assign it; its row version goes with the request. */
  asset: AssetDetails | null
  onClose: () => void
  onAssigned: (asset: AssetDetails) => void
  /** The asset changed meanwhile: the page shows it again so the user can decide on what it is now. */
  onConflict: () => void
}

/** "Zimmet Ver": picks an employee from Active Directory and records what they were given. */
export function AssignAssetDialog({ asset, onClose, onAssigned, onConflict }: AssignAssetDialogProps) {
  const {
    control,
    handleSubmit,
    reset,
    setError,
    formState: { isSubmitting },
  } = useForm<FormInput, unknown, FormValues>({ resolver: zodResolver(schema), defaultValues: emptyForm })
  const [failure, setFailure] = useState<Failure | null>(null)

  const close = () => {
    reset(emptyForm)
    setFailure(null)
    onClose()
  }

  const submit = handleSubmit(async (values) => {
    if (!asset || !values.employee) return
    setFailure(null)
    try {
      const saved = await assignAsset(
        asset.id,
        {
          employeeObjectGuid: values.employee.objectGuid,
          assignmentDescription: values.assignmentDescription,
          notes: values.notes === '' ? null : values.notes,
        },
        asset.rowVersion,
      )
      reset(emptyForm)
      onAssigned(saved)
    } catch (error) {
      const shown = showServerError(error, (field, message, focus) => setError(field, { message }, { shouldFocus: focus }))
      setFailure(shown)
      if (shown?.conflict) onConflict()
    }
  })

  return (
    <Dialog open={asset !== null} onClose={isSubmitting ? undefined : close} fullWidth maxWidth="sm">
      <Box component="form" noValidate onSubmit={submit} aria-label="Zimmet verme formu">
        <DialogTitle>Zimmet ver</DialogTitle>
        <DialogContent sx={{ display: 'grid', gap: 2, pt: '8px !important' }}>
          {failure && (
            <Alert severity="error" role="alert">
              <AlertTitle>{failure.title}</AlertTitle>
              {failure.detail}
              {failure.correlationId && <Box sx={{ mt: 0.5, fontSize: '0.8rem' }}>Hata kodu: {failure.correlationId}</Box>}
            </Alert>
          )}
          <Controller
            control={control}
            name="employee"
            render={({ field, fieldState }) => (
              <EmployeePicker
                value={field.value}
                onChange={field.onChange}
                onBlur={field.onBlur}
                inputRef={field.ref}
                error={fieldState.error?.message}
                disabled={isSubmitting}
              />
            )}
          />
          <Controller
            control={control}
            name="assignmentDescription"
            render={({ field: { ref, ...field }, fieldState }) => (
              <TextField
                {...field}
                inputRef={ref}
                label="Zimmet Tanımı"
                required
                fullWidth
                placeholder="Örn. Dizüstü bilgisayar + şarj adaptörü"
                disabled={isSubmitting}
                error={Boolean(fieldState.error)}
                helperText={fieldState.error?.message}
                slotProps={{ htmlInput: { maxLength: assignmentLimits.assignmentDescription } }}
              />
            )}
          />
          <Controller
            control={control}
            name="notes"
            render={({ field: { ref, ...field }, fieldState }) => (
              <TextField
                {...field}
                inputRef={ref}
                label="Not"
                fullWidth
                multiline
                minRows={2}
                disabled={isSubmitting}
                error={Boolean(fieldState.error)}
                helperText={fieldState.error?.message}
                slotProps={{ htmlInput: { maxLength: assignmentLimits.notes } }}
              />
            )}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={close} disabled={isSubmitting}>
            Vazgeç
          </Button>
          <Button
            type="submit"
            variant="contained"
            // After a conflict the user first looks at the asset as it is now.
            disabled={isSubmitting || failure?.conflict === true}
            startIcon={isSubmitting ? <CircularProgress size={18} color="inherit" aria-hidden="true" /> : undefined}
          >
            {isSubmitting ? 'Kaydediliyor...' : 'Zimmet Ver'}
          </Button>
        </DialogActions>
      </Box>
    </Dialog>
  )
}

/**
 * What a refused assignment means for the form: field messages go under their fields (the first one gets the
 * focus), anything else is returned for the alert above them.
 */
function showServerError(error: unknown, setFieldError: (field: keyof FormInput, message: string, focus: boolean) => void): Failure | null {
  if (!(error instanceof ApiError)) {
    return { title: 'Zimmet verilemedi', detail: 'Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.' }
  }

  const problem = error.problem
  if (error.code === 'concurrency_conflict') {
    return {
      title: 'Kayıt siz bakarken değiştirildi.',
      detail: 'Zimmet verilmedi. Sayfadaki bilgiler yenilendi; kontrol edip tekrar deneyin.',
      conflict: true,
    }
  }

  const fieldErrors = Object.entries(problem?.errors ?? {})
  if (fieldErrors.length > 0) {
    const others: string[] = []
    let first = true
    for (const [key, messages] of fieldErrors) {
      const field = apiFields[key]
      if (field) {
        setFieldError(field, messages[0], first)
        first = false
      } else {
        others.push(...messages)
      }
    }
    return others.length > 0 ? { title: problem?.title ?? 'Zimmet verilemedi', detail: others.join(' ') } : null
  }

  return {
    title: problem?.title ?? 'Zimmet verilemedi',
    detail: problem?.detail ?? 'Beklenmeyen bir hata oluştu. Tekrar deneyin.',
    correlationId: error.status >= 500 && error.status !== 503 ? problem?.correlationId : undefined,
    // The asset is no longer what the page showed (someone assigned, archived or broke it): the page shows it
    // as it is now. An inactive employee is not about the asset; another one can be chosen.
    conflict: error.code !== undefined && assetChangedCodes.has(error.code),
  }
}

const assetChangedCodes = new Set(['asset_already_assigned', 'asset_not_available', 'asset_archived', 'asset_not_found'])
