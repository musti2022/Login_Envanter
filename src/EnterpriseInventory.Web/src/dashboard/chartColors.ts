import { brandColors } from '../app/theme'

/**
 * Dashboard chart colors. Assignments and returns are two categories: brand blue and orange, checked with the
 * data-visualization palette validator on the white card (contrast at least 3:1, colour-blind separation ΔE 29.9).
 * "Rest" is a neutral, not a category: it is always written out in numbers beside the bar.
 */
export const chartColors = {
  assigned: brandColors.blue,
  returned: '#EB6834',
  rest: '#94A3B8',
} as const
