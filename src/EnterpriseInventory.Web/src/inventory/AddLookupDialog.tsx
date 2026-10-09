import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { ApiError, apiFetch } from '../api/http'
import { lookupsQueryKey, type LookupItem } from './lookupsApi'

export interface NewLookup {
  /** "marka", "model"... as the dialog title says it: "Yeni marka". */
  noun: string
  /** POST address, e.g. /api/brands. */
  path: string
  /** The brand of a new model or the city of a new location. */
  parent?: { field: 'brandId' | 'cityId'; id: number; name: string }
}

interface AddLookupDialogProps {
  lookup: NewLookup | null
  onClose: () => void
  onAdded: (item: LookupItem) => void
}

/** Adds a brand, model, city, location or department from the asset form, without leaving it. */
export function AddLookupDialog({ lookup, onClose, onAdded }: AddLookupDialogProps) {
  const queryClient = useQueryClient()
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const add = useMutation({
    mutationFn: (body: Record<string, unknown>) => apiFetch<LookupItem>(lookup!.path, { method: 'POST', body }),
  })

  const close = () => {
    setName('')
    setError(null)
    add.reset()
    onClose()
  }

  const submit = async () => {
    if (!lookup) return
    if (!name.trim()) {
      setError('Ad zorunludur.')
      return
    }
    try {
      const item = await add.mutateAsync({ name, ...(lookup.parent ? { [lookup.parent.field]: lookup.parent.id } : {}) })
      await queryClient.invalidateQueries({ queryKey: lookupsQueryKey })
      onAdded(item)
      close()
    } catch (failure) {
      const problem = failure instanceof ApiError ? failure.problem : undefined
      const fieldMessage = Object.values(problem?.errors ?? {})[0]?.[0]
      setError(fieldMessage ?? problem?.title ?? 'Kaydedilemedi. Bağlantınızı kontrol edip tekrar deneyin.')
    }
  }

  return (
    <Dialog open={lookup !== null} onClose={close} fullWidth maxWidth="xs">
      <DialogTitle>{lookup && `Yeni ${lookup.noun}`}</DialogTitle>
      <DialogContent>
        {lookup?.parent && (
          <Alert severity="info" sx={{ mb: 2 }}>
            {lookup.parent.name} altına eklenecek.
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
            setError(null)
          }}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault()
              void submit()
            }
          }}
          error={error !== null}
          helperText={error}
          slotProps={{ htmlInput: { maxLength: 100 } }}
        />
      </DialogContent>
      <DialogActions>
        <Button onClick={close}>Vazgeç</Button>
        <Button variant="contained" onClick={() => void submit()} disabled={add.isPending}>
          Ekle
        </Button>
      </DialogActions>
    </Dialog>
  )
}
