import { Alert, AlertTitle, Button, CircularProgress, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle } from '@mui/material'
import { useState } from 'react'
import { ApiError } from '../api/http'
import { returnAsset } from './assignmentsApi'
import type { AssetDetails } from './assetsApi'

interface ReturnAssetDialogProps {
  /** The asset as shown when the user asked to take it back; its row version goes with the request. */
  asset: AssetDetails | null
  onClose: () => void
  onReturned: (asset: AssetDetails) => void
  /** The asset changed meanwhile: the page shows it again so the user can decide on what it is now. */
  onConflict: () => void
}

/** "İade Al": closes the active assignment. The assignment stays in the asset's history. */
export function ReturnAssetDialog({ asset, onClose, onReturned, onConflict }: ReturnAssetDialogProps) {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<{ title: string; detail?: string; conflict?: boolean } | null>(null)

  const close = () => {
    setError(null)
    onClose()
  }

  const confirm = async () => {
    if (!asset) return
    setPending(true)
    setError(null)
    try {
      const saved = await returnAsset(asset.id, asset.rowVersion)
      onReturned(saved)
    } catch (failure) {
      const problem = failure instanceof ApiError ? failure.problem : undefined
      if (failure instanceof ApiError && failure.code === 'concurrency_conflict') {
        setError({
          title: 'Kayıt siz bakarken değiştirildi.',
          detail: 'İade alınmadı. Sayfadaki bilgiler yenilendi; kontrol edip tekrar deneyin.',
          conflict: true,
        })
        onConflict()
      } else if (failure instanceof ApiError && failure.status === 409) {
        setError({ title: problem?.title ?? 'İade alınamadı', detail: problem?.detail, conflict: true })
        onConflict()
      } else {
        setError({
          title: problem?.title ?? 'İade alınamadı',
          detail: problem?.detail ?? 'Bağlantınızı kontrol edip tekrar deneyin.',
        })
      }
    } finally {
      setPending(false)
    }
  }

  const holder = asset?.activeAssignment
  return (
    <Dialog open={asset !== null} onClose={pending ? undefined : close} fullWidth maxWidth="xs">
      <DialogTitle>İade al</DialogTitle>
      <DialogContent>
        {error && (
          <Alert severity="error" role="alert" sx={{ mb: 2 }}>
            <AlertTitle>{error.title}</AlertTitle>
            {error.detail}
          </Alert>
        )}
        <DialogContentText>
          {asset?.assetCode}
          {holder ? `, ${holder.displayName} (${holder.userName}) adlı çalışandan` : ''} iade alınacak ve demirbaş Boşta
          olacak. Zimmet kaydı silinmez; zimmet geçmişinde kalır.
        </DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={close} disabled={pending}>
          Vazgeç
        </Button>
        <Button
          variant="contained"
          onClick={() => void confirm()}
          // After a conflict the user first looks at the asset as it is now.
          disabled={pending || error?.conflict === true}
          startIcon={pending ? <CircularProgress size={18} color="inherit" aria-hidden="true" /> : undefined}
        >
          İade Al
        </Button>
      </DialogActions>
    </Dialog>
  )
}
