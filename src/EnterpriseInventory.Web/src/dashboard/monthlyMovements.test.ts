import { describeMonth, scaleTop } from './monthlyMovements'

describe('scaleTop', () => {
  it.each([
    [0, 2],
    [1, 2],
    [3, 4],
    [7, 8],
    [10, 10],
    [11, 20],
    [45, 60],
    [101, 200],
    [9001, 10000],
  ])('puts the top of the scale for %i at %i', (largest, top) => {
    expect(scaleTop(largest)).toBe(top)
    // The middle line is a whole number.
    expect(Number.isInteger(top / 2)).toBe(true)
  })
})

describe('describeMonth', () => {
  it('names the month in Turkish with its numbers', () => {
    expect(describeMonth({ month: '2026-02', assignedCount: 1250, returnedCount: 0 })).toBe('Şubat 2026: 1.250 zimmet, 0 iade')
  })
})
