import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// The k6 scenario imports k6/http, which Node cannot load, so this test reads tests/load/objectives.js as text. It keeps
// the routes that docs/reference/performance.md lists for the read and write groups equal to the routes that the scenario
// tags with those objectives.
const root = fileURLToPath(new URL('../..', import.meta.url))
const scenario = readFileSync(`${root}tests/load/objectives.js`, 'utf8')
const performance = readFileSync(`${root}docs/reference/performance.md`, 'utf8')

// "group|METHOD /template" for each row of the route table: | Reads | `GET /api/...` |
const listedRoutes = () => {
  const routes = [...performance.matchAll(/^\|\s*(Reads|Writes)\s*\|\s*`([A-Z]+ \/[^`]*)`\s*\|\s*$/gm)].map((match) => `${match[1].toLowerCase()}|${match[2]}`)
  return routes
}

// The same, for each call of send() whose objective, method and route template are string literals.
const taggedRoutes = () =>
  [...scenario.matchAll(/send\(\s*'(reads|writes|signIn)',\s*'([A-Z]+)',\s*'([^']+)'/g)]
    .filter((match) => match[1] !== 'signIn')
    .map((match) => `${match[1]}|${match[2]} ${match[3]}`)

const signInRoutes = () => [...scenario.matchAll(/send\(\s*'signIn',\s*'([A-Z]+)',\s*'([^']+)'/g)].length

describe('the routes in performance.md and the calls of tests/load/objectives.js', () => {
  test('performance.md lists each route once', () => {
    const listed = listedRoutes()
    assert.ok(listed.length > 0, 'performance.md lists no route')
    assert.equal(new Set(listed).size, listed.length, listed.join('\n'))
  })

  test('every listed route is sent by the scenario, and every route that the scenario sends is listed', () => {
    const listed = new Set(listedRoutes())
    const tagged = new Set(taggedRoutes())
    assert.deepEqual(
      [...listed].filter((route) => !tagged.has(route)),
      [],
      'listed in performance.md but not sent by objectives.js',
    )
    assert.deepEqual(
      [...tagged].filter((route) => !listed.has(route)),
      [],
      'sent by objectives.js but not listed in performance.md',
    )
  })

  test('the scenario sends both groups', () => {
    const groups = new Set(taggedRoutes().map((route) => route.split('|')[0]))
    assert.deepEqual([...groups].sort(), ['reads', 'writes'])
  })

  test('http.request is called once, in send(), and no other http call sends a request', () => {
    assert.equal(scenario.match(/\bhttp\.request\(/g)?.length, 1)
    assert.ok(/function send\([^)]*\) \{[\s\S]*?\bhttp\.request\(/.test(scenario), 'http.request is not in send()')
    assert.equal(scenario.match(/\bhttp\.(get|post|put|patch|del|options|head|batch|asyncRequest)\(/g), null)
  })

  test('every call of send() names its objective, method and route template as string literals', () => {
    const code = scenario.replace(/^\s*\/\/.*$/gm, '').replace('function send(', 'function sender(')
    assert.equal(code.match(/\bsend\(/g).length, taggedRoutes().length + signInRoutes())
  })
})
