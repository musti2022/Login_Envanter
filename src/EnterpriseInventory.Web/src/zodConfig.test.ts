import { z } from 'zod'
import './zodConfig'

describe('zod configuration', () => {
  it('never compiles validators with eval, which the Content-Security-Policy forbids', () => {
    expect(z.config().jitless).toBe(true)
  })
})
