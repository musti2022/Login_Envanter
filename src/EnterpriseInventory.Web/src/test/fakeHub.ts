import type { InventoryConnection, InventoryConnectionOptions } from '../realtime/inventoryHub'

type Handler = (...args: unknown[]) => void

/**
 * Stands in for the SignalR connection in every test (see setup.ts): nothing goes over the network, and a test can
 * play the hub's part by announcing changes or closing the connection.
 */
export class FakeHubConnection implements InventoryConnection {
  readonly handlers = new Map<string, Handler[]>()
  readonly closeHandlers: ((error?: Error) => void)[] = []
  readonly options: InventoryConnectionOptions
  starts = 0
  stopped = false

  /** What start() does; by default it connects at once. */
  startResult: () => Promise<void> = () => Promise.resolve()

  constructor(options: InventoryConnectionOptions) {
    this.options = options
  }

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

  /** The hub announces a change. */
  emit(name: string, ...args: unknown[]) {
    for (const handler of this.handlers.get(name) ?? []) {
      handler(...args)
    }
  }

  /** The connection is lost, e.g. the API restarted. */
  close(error?: Error) {
    for (const handler of this.closeHandlers) {
      handler(error)
    }
  }

  /** The API refuses the connection because the session has ended, as the real connection's requests report it. */
  refuseEndedSession() {
    this.options.onUnauthorized()
    return Promise.reject(new Error('Failed to complete negotiation with the server: Error: Unauthorized: Status code \'401\''))
  }
}

/** The connections the app created, newest last. */
export const fakeHub = {
  connections: [] as FakeHubConnection[],
  /** Applied to each new connection, e.g. to make start() fail. */
  configure: (connection: FakeHubConnection) => void connection,

  create(options: InventoryConnectionOptions): FakeHubConnection {
    const connection = new FakeHubConnection(options)
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
