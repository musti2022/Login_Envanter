import { Alert, AlertTitle, Button, CircularProgress, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle } from '@mui/material'
import { useState } from 'react'
import { ApiError } from '../api/http'
import { archiveAsset, type AssetDetails } from './assetsApi'

interface ArchiveAssetDialogProps {
  /** The asset as shown when the user asked to archive it; its row version goes with the request. */
  asset: AssetDetails | null
  onClose: () => void
  onArchived: () => void
  /** The asset changed meanwhile: the page shows it again so the user can decide on what it is now. */
  onConflict: () => void
}

export function ArchiveAssetDialog({ asset, onClose, onArchived, onConflict }: ArchiveAssetDialogProps) {
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
      await archiveAsset(asset.id, asset.rowVersion)
      onArchived()
    } catch (failure) {
      const problem = failure instanceof ApiError ? failure.problem : undefined
      if (failure instanceof ApiError && failure.code === 'concurrency_conflict') {
        setError({
          title: 'Kayıt siz bakarken değiştirildi.',
          detail: 'Arşivlenmedi. Sayfadaki bilgiler yenilendi; kontrol edip tekrar deneyin.',
          conflict: true,
        })
        onConflict()
      } else {
        setError({
          title: problem?.title ?? 'Arşivlenemedi',
          detail: problem?.detail ?? 'Bağlantınızı kontrol edip tekrar deneyin.',
        })
      }
    } finally {
      setPending(false)
    }
  }

  return (
    <Dialog open={asset !== null} onClose={pending ? undefined : close} fullWidth maxWidth="xs">
      <DialogTitle>Demirbaşı arşivle</DialogTitle>
      <DialogContent>
        {error && (
          <Alert severity="error" role="alert" sx={{ mb: 2 }}>
            <AlertTitle>{error.title}</AlertTitle>
            {error.detail}
          </Alert>
        )}
        <DialogContentText>
          {asset?.assetCode} arşivlenecek. Arşivlenen demirbaş listede ve sayılarda görünmez, düzenlenemez; geçmişi
          korunur ve "Arşivlenmişleri göster" ile bulunabilir.
        </DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={close} disabled={pending}>
          Vazgeç
        </Button>
        <Button
          color="error"
          variant="contained"
          onClick={() => void confirm()}
          // After a conflict the user first looks at the asset as it is now.
          disabled={pending || error?.conflict === true}
          startIcon={pending ? <CircularProgress size={18} color="inherit" aria-hidden="true" /> : undefined}
        >
          Arşivle
        </Button>
      </DialogActions>
    </Dialog>
  )
}
