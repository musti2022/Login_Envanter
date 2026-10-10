import '@testing-library/jest-dom/vitest'
import { fakeHub } from './fakeHub'
import type { InventoryConnectionOptions } from '../realtime/inventoryHub'

// No test opens a real live connection; tests that need the hub drive the fake (see fakeHub.ts).
vi.mock('../realtime/inventoryHub', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../realtime/inventoryHub')>()),
  createInventoryConnection: (options: InventoryConnectionOptions) => fakeHub.create(options),
}))

afterEach(() => fakeHub.reset())
