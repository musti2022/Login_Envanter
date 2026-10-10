import { formatNumber } from '../inventory/labels'
import type { MonthlyMovement } from './dashboardApi'

export const shortMonth = new Intl.DateTimeFormat('tr-TR', { month: 'short' })
export const longMonth = new Intl.DateTimeFormat('tr-TR', { month: 'long', year: 'numeric' })

/** "2026-10" as the first day of that month, on the reader's calendar (only its name is shown). */
export function dateOf(month: string) {
  const [year, monthNumber] = month.split('-').map(Number)
  return new Date(year, monthNumber - 1, 1)
}

/** "Ekim 2026: 5 zimmet, 3 iade" */
export function describeMonth(movement: MonthlyMovement) {
  return `${longMonth.format(dateOf(movement.month))}: ${formatNumber(movement.assignedCount)} zimmet, ${formatNumber(movement.returnedCount)} iade`
}

/** The top of the scale: the smallest of 2, 4, 6, 8, 10 × 10ⁿ that holds the largest value, so the middle line is whole. */
export function scaleTop(largest: number) {
  for (let magnitude = 1; ; magnitude *= 10) {
    const top = [2, 4, 6, 8, 10].map((step) => step * magnitude).find((candidate) => candidate >= largest)
    if (top !== undefined) return top
  }
}
