import FileDownloadIcon from '@mui/icons-material/FileDownload'
import { Alert, AlertTitle, Button, CircularProgress, Snackbar } from '@mui/material'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { ApiError } from '../api/http'
import { saveFile } from '../api/saveFile'
import { exportAssets, type AssetListParams } from './assetsApi'

interface Failure {
  title: string
  detail: string
}

interface ExportButtonProps {
  /** The list on screen: the file holds every page of it, with the same filters and order. */
  params: AssetListParams
  disabled?: boolean
}

/** "Excel'e aktar": downloads the inventory list as it is filtered and sorted on screen. */
export function ExportButton({ params, disabled = false }: ExportButtonProps) {
  const [failure, setFailure] = useState<Failure | null>(null)
  const download = useMutation({
    mutationFn: () => exportAssets(params),
    onMutate: () => setFailure(null),
    onSuccess: saveFile,
    onError: (error) => setFailure(failureOf(error)),
  })

  return (
    <>
      <Button
        variant="outlined"
        startIcon={download.isPending ? <CircularProgress size={18} color="inherit" aria-hidden /> : <FileDownloadIcon />}
        onClick={() => download.mutate()}
        disabled={disabled || download.isPending}
      >
        {download.isPending ? 'Hazırlanıyor...' : "Excel'e aktar"}
      </Button>
      <Snackbar
        open={failure !== null}
        onClose={(_, reason) => reason !== 'clickaway' && setFailure(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert severity="error" variant="filled" onClose={() => setFailure(null)} sx={{ maxWidth: 560 }}>
          <AlertTitle>{failure?.title}</AlertTitle>
          {failure?.detail}
        </Alert>
      </Snackbar>
    </>
  )
}

/** A refused export in Turkish; the server explains a list too long for one file. */
function failureOf(error: unknown): Failure | null {
  if (!(error instanceof ApiError)) {
    return { title: 'Excel dosyası hazırlanamadı', detail: 'Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.' }
  }

  // The session has ended: the sign-in page takes over.
  if (error.status === 401) return null

  if (error.status === 429) {
    const wait = error.retryAfterSeconds ? `${error.retryAfterSeconds} saniye sonra` : 'biraz sonra'
    return { title: 'Çok fazla istek gönderildi', detail: `Lütfen ${wait} tekrar deneyin.` }
  }

  return {
    title: error.problem?.title ?? 'Excel dosyası hazırlanamadı',
    detail: error.problem?.detail ?? 'Beklenmeyen bir hata oluştu. Tekrar deneyin.',
  }
}
