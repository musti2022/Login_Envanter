import type { DownloadedFile } from './http'

/** Hands a downloaded file to the browser, which saves it like any other download. */
export function saveFile({ blob, fileName }: DownloadedFile) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.style.display = 'none'
  document.body.append(link)
  link.click()
  link.remove()
  // Some browsers read the address after the click has returned; it is released once the save has surely started.
  window.setTimeout(() => URL.revokeObjectURL(url), 10_000)
}
