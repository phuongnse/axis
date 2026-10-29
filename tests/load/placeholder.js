// The placeholder scenario of scripts/load.sh. It proves that k6 reaches the tenant `load` through the TLS proxy
// and the server replicas: without a signed-in user, GET /api/me answers 401. Later parts of the load test replace
// it with the generated application, signed-in users and the objectives of docs/reference/performance.md.
import http from 'k6/http'
import { check } from 'k6'

export const options = {
  vus: 1,
  iterations: 10,
  thresholds: { checks: ['rate==1'] },
}

export default function () {
  const response = http.get(`${__ENV.AXIS_LOAD_URL}/api/me`, { redirects: 0 })
  check(response, { 'the tenant answers 401 without a user': (r) => r.status === 401 })
}
