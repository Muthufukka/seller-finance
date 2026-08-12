const url = process.argv[2] || 'https://seller-finance.onrender.com/health'
const requests = Number(process.argv[3] || 40)
const concurrency = Number(process.argv[4] || 5)
const p95LimitMs = Number(process.argv[5] || 800)
if (!Number.isInteger(requests) || requests < 1 || requests > 500 || !Number.isInteger(concurrency) || concurrency < 1 || concurrency > 20) throw new Error('Unsafe request/concurrency value')

await fetch(url, { signal: AbortSignal.timeout(60_000) })
const durations = []
let cursor = 0
async function worker() {
  while (cursor < requests) {
    cursor++
    const started = performance.now()
    const response = await fetch(url, { signal: AbortSignal.timeout(15_000), headers: { 'user-agent': 'seller-finance-safe-load-smoke/1.0' } })
    durations.push(performance.now() - started)
    if (!response.ok) throw new Error(`Load request failed with ${response.status}`)
    await response.arrayBuffer()
  }
}
await Promise.all(Array.from({ length: concurrency }, worker))
durations.sort((a, b) => a - b)
const percentile = p => durations[Math.min(durations.length - 1, Math.ceil(durations.length * p) - 1)]
const p50 = percentile(.5), p95 = percentile(.95), max = durations.at(-1)
console.log(JSON.stringify({ url, requests, concurrency, p50Ms: Math.round(p50), p95Ms: Math.round(p95), maxMs: Math.round(max) }))
if (p95 > p95LimitMs) throw new Error(`P95 ${Math.round(p95)}ms exceeds ${p95LimitMs}ms`)

