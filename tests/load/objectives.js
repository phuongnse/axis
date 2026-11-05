// The load test of docs/reference/performance.md#load-test, run by scripts/load.sh. 50 virtual users each sign in once
// through the page of the realm `load`, with a password and then the authenticator code of users.json, and keep their
// session cookie. They then loop over the objectives. The loop is in tests/load/lib/objectives-loop.js, which the cell
// load test (tests/load/cell.js) shares, so both send the same requests. The thresholds below measure the routes of the loop.
import { sleep } from 'k6'
import { configure, objectivesIteration } from './lib/objectives-loop.js'

export const options = {
  vus: 50,
  duration: '5m',
  // k6 empties the cookie jar of a user at the start of each iteration, which would end the session after sign-in.
  noCookiesReset: true,
  thresholds: {
    'http_req_duration{objective:reads}': ['p(95)<300'],
    'http_req_duration{objective:writes}': ['p(95)<500'],
    server_errors: ['rate<0.001'],
    // Without it, a run where every request answers 401 or 403 would still meet the thresholds above.
    checks: ['rate>0.99'],
  },
}

const users = JSON.parse(open(__ENV.AXIS_LOAD_USERS))
const caller = JSON.parse(open(__ENV.AXIS_LOAD_CALLER))
configure({ url: __ENV.AXIS_LOAD_URL, tokenUrl: __ENV.AXIS_LOAD_TOKEN_URL, caller })

export default function () {
  objectivesIteration({ user: users[(__VU - 1) % users.length], number: __VU - 1, iteration: __ITER })
  sleep(1)
}
