import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'

// The k6 scenarios import k6/http, which Node cannot load, so this test reads the files as text. The loop of the objectives
// is in tests/load/lib/objectives-loop.js, which both the load test and the cell load test run. It keeps the routes that
// docs/reference/performance.md lists for the read and write groups equal to the routes that the loop tags with those
// objectives, and it checks that neither scenario sends a request itself.
const root = fileURLToPath(new URL('../..', import.meta.url))
const loop = readFileSync(`${root}tests/load/lib/objectives-loop.js`, 'utf8')
const performance = readFileSync(`${root}docs/reference/performance.md`, 'utf8')

// "group|METHOD /template" for each row of the route table: | Reads | `GET /api/...` |
const listedRoutes = () => {
  const routes = [...performance.matchAll(/^\|\s*(Reads|Writes)\s*\|\s*`([A-Z]+ \/[^`]*)`\s*\|\s*$/gm)].map((match) => `${match[1].toLowerCase()}|${match[2]}`)
  return routes
}

// The same, for each call of send() whose objective, method and route template are string literals.
const taggedRoutes = () =>
  [...loop.matchAll(/send\(\s*'(reads|writes|signIn)',\s*'([A-Z]+)',\s*'([^']+)'/g)]
    .filter((match) => match[1] !== 'signIn')
    .map((match) => `${match[1]}|${match[2]} ${match[3]}`)

const signInRoutes = () => [...loop.matchAll(/send\(\s*'signIn',\s*'([A-Z]+)',\s*'([^']+)'/g)].length

const withoutComments = (code) => code.replace(/^\s*\/\/.*$/gm, '')

describe('the routes in performance.md and the calls of tests/load/lib/objectives-loop.js', () => {
  test('performance.md lists each route once', () => {
    const listed = listedRoutes()
    assert.ok(listed.length > 0, 'performance.md lists no route')
    assert.equal(new Set(listed).size, listed.length, listed.join('\n'))
  })

  test('every listed route is sent by the loop, and every route that the loop sends is listed', () => {
    const listed = new Set(listedRoutes())
    const tagged = new Set(taggedRoutes())
    assert.deepEqual(
      [...listed].filter((route) => !tagged.has(route)),
      [],
      'listed in performance.md but not sent by objectives-loop.js',
    )
    assert.deepEqual(
      [...tagged].filter((route) => !listed.has(route)),
      [],
      'sent by objectives-loop.js but not listed in performance.md',
    )
  })

  test('the loop sends both groups', () => {
    const groups = new Set(taggedRoutes().map((route) => route.split('|')[0]))
    assert.deepEqual([...groups].sort(), ['reads', 'writes'])
  })

  test('http.request is called once, in send(), and no other http call sends a request', () => {
    assert.equal(loop.match(/\bhttp\.request\(/g)?.length, 1)
    assert.ok(/function send\([^)]*\) \{[\s\S]*?\bhttp\.request\(/.test(loop), 'http.request is not in send()')
    assert.equal(loop.match(/\bhttp\.(get|post|put|patch|del|options|head|batch|asyncRequest)\(/g), null)
  })

  test('every call of send() names its objective, method and route template as string literals', () => {
    const code = withoutComments(loop).replace('function send(', 'function sender(')
    assert.equal(code.match(/\bsend\(/g).length, taggedRoutes().length + signInRoutes())
  })

  test('objectives.js and cell.js send no request themselves, and call the loop instead', () => {
    for (const file of ['tests/load/objectives.js', 'tests/load/cell.js']) {
      const code = withoutComments(readFileSync(`${root}${file}`, 'utf8'))
      assert.doesNotMatch(code, /\bsend\(/, file)
      assert.doesNotMatch(code, /\bhttp\./, file)
      assert.match(code, /from '\.\/lib\/objectives-loop\.js'/, file)
    }
  })
})
