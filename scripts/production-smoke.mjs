const base = (process.argv[2] || 'https://seller-finance.onrender.com').replace(/\/$/, '')
const expectedRevision = process.argv[3]?.trim()

async function request(path, expectedStatus, options = {}) {
  const started = performance.now()
  const response = await fetch(base + path, { redirect: 'manual', signal: AbortSignal.timeout(60_000), ...options })
  const duration = Math.round(performance.now() - started)
  if (response.status !== expectedStatus) throw new Error(`${path}: expected ${expectedStatus}, got ${response.status}`)
  return { response, duration }
}

const root = await request('/', 200)
const html = await root.response.text()
const asset = html.match(/\/assets\/index-[A-Za-z0-9_-]+\.js/)?.[0]
if (!asset) throw new Error('Frontend asset was not found in index.html')
await request(asset, 200)

for (const path of ['/health', '/health/database', '/health/ready']) {
  const result = await request(path, 200)
  const body = await result.response.json()
  if (!['healthy', 'ready'].includes(body.status)) throw new Error(`${path}: unhealthy response`)
  if (path === '/health' && expectedRevision && body.revision !== expectedRevision) throw new Error(`Expected revision ${expectedRevision}, got ${body.revision}`)
  console.log(`${path} OK ${result.duration}ms`)
}

for (const path of ['/api/v1/session', '/api/v1/kaspi/connections', '/api/v1/admin/organizations']) await request(path, 401)
const deletion = await request('/api/v1/organizations/not-authorized', 401, { method: 'DELETE', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ organizationName: 'none', password: 'none' }) })
if (deletion.response.status !== 401) throw new Error('Destructive endpoint is not protected')

for (const header of ['content-security-policy', 'x-content-type-options', 'referrer-policy']) if (!root.response.headers.get(header)) throw new Error(`Missing security header: ${header}`)
console.log(`Production smoke passed; frontend ${asset}`)

