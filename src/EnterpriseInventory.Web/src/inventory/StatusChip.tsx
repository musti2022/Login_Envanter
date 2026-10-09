import { Chip, type ChipProps } from '@mui/material'
import { statusLabels, type AssetStatus } from './labels'

const colors: Record<AssetStatus, ChipProps['color']> = {
  Available: 'success',
  Assigned: 'primary',
  Faulty: 'warning',
  Retired: 'default',
}

/** The status as a chip; the Turkish label is always written, so colour is never the only signal. */
export function StatusChip({ status }: { status: AssetStatus }) {
  return <Chip size="small" variant="outlined" color={colors[status]} label={statusLabels[status]} />
}
