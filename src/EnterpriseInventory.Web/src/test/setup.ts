import '@testing-library/jest-dom/vitest'
import { fakeHub } from './fakeHub'

// No test opens a real live connection; tests that need the hub drive the fake (see fakeHub.ts).
vi.mock('../realtime/inventoryHub', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../realtime/inventoryHub')>()),
  createInventoryConnection: () => fakeHub.create(),
}))

afterEach(() => fakeHub.reset())
