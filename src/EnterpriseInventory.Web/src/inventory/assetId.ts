/** The asset ID of an address like /envanter/12; null when it is not a positive whole number. */
export function parseAssetId(value: string | undefined): number | null {
  if (!value || !/^\d{1,9}$/.test(value)) return null
  const id = Number(value)
  return id > 0 ? id : null
}
