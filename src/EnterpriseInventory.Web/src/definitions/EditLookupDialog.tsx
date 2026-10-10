import {
  Alert,
  AlertTitle,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  FormHelperText,
  Switch,
  TextField,
} from '@mui/material'
import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { ApiError } from '../api/http'
import { lookupsQueryKey, updateLookup, type LookupItem } from '../inventory/lookupsApi'

interface EditLookupDialogProps {
  /** "marka", "model"... as the title says it: "Marka düzenle". */
  noun: string
  /** The list's address, e.g. /api/brands; the change goes to /api/brands/{id}. */
  path: string
  /** The lookup as listed when the user chose to edit it; its row version goes with the change. */
  item: LookupItem
  /** "Marka: Dell (değiştirilemez)": the brand of a model or the city of a location, which does not change here. */
  parentNote?: string
  onClose: () => void
}

interface Failure {
  title: string
  detail?: string
  conflict?: boolean
}

/**
 * Renames, deactivates or reactivates a lookup. Mounted per item (the caller keys it), so it always starts from the
 * values the list showed. A change someone else saved meanwhile is never overwritten: the API answers 409, the
 * dialog says so and the list is fetched again.
 */
export function EditLookupDialog({ noun, path, item, parentNote, onClose }: EditLookupDialogProps) {
  const queryClient = useQueryClient()
  const [name, setName] = useState(item.name)
  const [isActive, setIsActive] = useState(item.isActive)
  const [nameError, setNameError] = useState<string | null>(null)
  const [stateError, setStateError] = useState<string | null>(null)
  const [failure, setFailure] = useState<Failure | null>(null)
  const [pending, setPending] = useState(false)
  const title = `${noun.charAt(0).toLocaleUpperCase('tr-TR')}${noun.slice(1)} düzenle`

  const save = async () => {
    if (!name.trim()) {
      setNameError('Ad zorunludur.')
      return
    }
    setPending(true)
    setFailure(null)
    try {
      await updateLookup(path, item.id, { name, isActive, rowVersion: item.rowVersion })
      // Asset lists, reports and filters show the names too.
      await queryClient.invalidateQueries()
      onClose()
    } catch (error) {
      const problem = error instanceof ApiError ? error.problem : undefined
      if (error instanceof ApiError && error.code === 'concurrency_conflict') {
        setFailure({
          title: 'Kayıt siz düzenlerken başka bir kullanıcı tarafından değiştirildi.',
          detail: 'Değişiklikleriniz kaydedilmedi. Liste yenilendi; kaydı yeniden açıp güncel bilgiler üzerinde tekrar deneyin.',
          conflict: true,
        })
        void queryClient.invalidateQueries({ queryKey: lookupsQueryKey })
      } else if (problem?.errors?.name?.[0]) {
        setNameError(problem.errors.name[0])
      } else if (problem?.errors?.isActive?.[0]) {
        setStateError(problem.errors.isActive[0])
      } else {
        setFailure({
          title: problem?.title ?? 'Kaydedilemedi',
          detail: problem?.detail ?? 'Bağlantınızı kontrol edip tekrar deneyin.',
        })
      }
    } finally {
      setPending(false)
    }
  }

  return (
    <Dialog open onClose={pending ? undefined : onClose} fullWidth maxWidth="xs">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        {failure && (
          <Alert severity="error" role="alert" sx={{ mb: 2 }}>
            <AlertTitle>{failure.title}</AlertTitle>
            {failure.detail}
          </Alert>
        )}
        {parentNote && (
          <Alert severity="info" sx={{ mb: 2 }}>
            {parentNote}
          </Alert>
        )}
        <TextField
          autoFocus
          fullWidth
          margin="dense"
          label="Ad"
          value={name}
          onChange={(event) => {
            setName(event.target.value)
            setNameError(null)
          }}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault()
              void save()
            }
          }}
          error={nameError !== null}
          helperText={nameError}
          slotProps={{ htmlInput: { maxLength: 100 } }}
        />
        <FormControlLabel
          sx={{ mt: 1 }}
          control={
            <Switch
              checked={isActive}
              onChange={(event) => {
                setIsActive(event.target.checked)
                setStateError(null)
              }}
            />
          }
          label={isActive ? 'Aktif' : 'Pasif'}
        />
        <FormHelperText error={stateError !== null}>
          {stateError ??
            (isActive
              ? 'Yeni kayıtlarda seçilebilir.'
              : 'Yeni kayıtlarda seçilemez; onu kullanan demirbaşlar ve geçmiş değişmez.')}
        </FormHelperText>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={pending}>
          Vazgeç
        </Button>
        <Button
          variant="contained"
          onClick={() => void save()}
          // After a conflict the user first looks at the record as it is now.
          disabled={pending || failure?.conflict === true}
          startIcon={pending ? <CircularProgress size={18} color="inherit" aria-hidden="true" /> : undefined}
        >
          Kaydet
        </Button>
      </DialogActions>
    </Dialog>
  )
}
