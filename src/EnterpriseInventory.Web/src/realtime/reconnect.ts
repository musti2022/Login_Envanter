/** Waits before the next try, in ms: at once after a connection was lost, then backing off to every 30 seconds. */
const delays = [0, 2_000, 5_000, 10_000, 30_000]

/**
 * How long to wait before trying to connect again, given how many tries have failed since the app was last
 * connected. Tries never stop while the app is shown: an outage of the API ends on its own, and the header says
 * meanwhile that changes do not show up.
 */
export function reconnectDelay(failedTries: number): number {
  return delays[Math.min(failedTries, delays.length - 1)]
}
