import { z } from 'zod'

// The pages' Content-Security-Policy forbids eval. Without this zod probes `new Function` once to compile faster
// validators, and the browser reports that probe as a policy violation. Imported first in main.tsx, since the probe
// runs when the first schema is built.
z.config({ jitless: true })
