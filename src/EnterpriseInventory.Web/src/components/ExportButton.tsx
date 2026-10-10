import FileDownloadIcon from '@mui/icons-material/FileDownload'
import { Alert, AlertTitle, Button, CircularProgress, Snackbar } from '@mui/material'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { ApiError, type DownloadedFile } from '../api/http'
import { saveFile } from '../api/saveFile'

interface Failure {
  title: string
  detail: string
}

interface ExportButtonProps {
  /** Asks the API for the file of what is on screen (every page of it, with the same filters and order). */
  download: () => Promise<DownloadedFile>
  disabled?: boolean
}

/** "Excel'e aktar": downloads a list or report as it is filtered on screen; a refused file is explained in Turkish. */
export function ExportButton({ download: request, disabled = false }: ExportButtonProps) {
  const [failure, setFailure] = useState<Failure | null>(null)
  const download = useMutation({
    mutationFn: request,
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
