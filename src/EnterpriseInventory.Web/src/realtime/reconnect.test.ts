import { reconnectDelay } from './reconnect'

describe('Reconnect delay', () => {
  it('try at once, then back off to every 30 seconds without giving up', () => {
    expect([0, 1, 2, 3, 4, 5, 50].map(reconnectDelay)).toEqual([0, 2_000, 5_000, 10_000, 30_000, 30_000, 30_000])
  })
})
