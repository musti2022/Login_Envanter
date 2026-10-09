import type { InventoryConnection } from '../realtime/inventoryHub'

type Handler = (...args: unknown[]) => void

/**
 * Stands in for the SignalR connection in every test (see setup.ts): nothing goes over the network, and a test can
 * play the hub's part by announcing changes or closing the connection.
 */
export class FakeHubConnection implements InventoryConnection {
  readonly handlers = new Map<string, Handler[]>()
  readonly closeHandlers: ((error?: Error) => void)[] = []
  readonly reconnectingHandlers: ((error?: Error) => void)[] = []
  readonly reconnectedHandlers: ((connectionId?: string) => void)[] = []
  starts = 0
  stopped = false

  /** What start() does; by default it connects at once. */
  startResult: () => Promise<void> = () => Promise.resolve()

  start = () => {
    this.starts++
    return this.startResult()
  }

  stop = () => {
    this.stopped = true
    return Promise.resolve()
  }

  on = (name: string, handler: Handler) => {
    this.handlers.set(name, [...(this.handlers.get(name) ?? []), handler])
  }

  onclose = (handler: (error?: Error) => void) => {
    this.closeHandlers.push(handler)
  }

  onreconnecting = (handler: (error?: Error) => void) => {
    this.reconnectingHandlers.push(handler)
  }

  onreconnected = (handler: (connectionId?: string) => void) => {
    this.reconnectedHandlers.push(handler)
  }

  /** The hub announces a change. */
  emit(name: string, ...args: unknown[]) {
    for (const handler of this.handlers.get(name) ?? []) {
      handler(...args)
    }
  }

  close(error?: Error) {
    for (const handler of this.closeHandlers) {
      handler(error)
    }
  }
}

/** The connections the app created, newest last. */
export const fakeHub = {
  connections: [] as FakeHubConnection[],
  /** Applied to each new connection, e.g. to make start() fail. */
  configure: (connection: FakeHubConnection) => void connection,

  create(): FakeHubConnection {
    const connection = new FakeHubConnection()
    this.configure(connection)
    this.connections.push(connection)
    return connection
  },

  latest(): FakeHubConnection {
    const connection = this.connections.at(-1)
    if (!connection) throw new Error('The app opened no live connection.')
    return connection
  },

  reset() {
    this.connections = []
    this.configure = () => {}
  },
}
