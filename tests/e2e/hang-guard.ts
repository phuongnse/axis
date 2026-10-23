import { execFile } from 'node:child_process'
import os from 'node:os'
import type { Page, TestInfo } from '@playwright/test'

/** How long the diagnostics may take. A hung browser can leave a screenshot waiting for good. */
const diagnosticsLimit = 10_000

/** Settles with `undefined` when `work` does not finish within `limit` ms, so a hung call never holds the caller. */
async function within<T>(limit: number, work: Promise<T>): Promise<T | undefined> {
  let timer: NodeJS.Timeout | undefined
  const expired = new Promise<undefined>((resolve) => {
    timer = setTimeout(() => resolve(undefined), limit)
  })
  try {
    return await Promise.race([work, expired])
  } finally {
    clearTimeout(timer)
  }
}

/**
 * Runs `work` and returns its result. When `work` runs longer than `limit` ms it does not wait further: it calls
 * `describeHang` for the evidence and throws an Error that names `step`, the limit and that evidence. The work itself
 * cannot be cancelled, so a later failure of it is ignored.
 */
export async function failAfter<T>(
  step: string,
  limit: number,
  work: () => Promise<T>,
  describeHang: () => Promise<string>,
): Promise<T> {
  const started = Date.now()
  let timer: NodeJS.Timeout | undefined
  const running = work()
  const hung = Symbol('hung')
  const expired = new Promise<typeof hung>((resolve) => {
    timer = setTimeout(() => resolve(hung), limit)
  })
  try {
    const outcome = await Promise.race([running, expired])
    if (outcome !== hung) return outcome
  } finally {
    clearTimeout(timer)
  }
  running.catch(() => {})
  const evidence = await describeHang()
  throw new Error(`${step} did not finish within ${limit / 1000} seconds (${Date.now() - started} ms).\n${evidence}`)
}

/** The busiest processes and the load of this machine, to tell a starved runner from a stuck browser. */
export async function machineState(): Promise<string> {
  const lines = [
    `load average (1, 5, 15 min): ${os.loadavg()
      .map((load) => load.toFixed(2))
      .join(', ')} on ${os.availableParallelism()} CPUs`,
    `free memory: ${Math.round(os.freemem() / 2 ** 20)} of ${Math.round(os.totalmem() / 2 ** 20)} MiB`,
  ]
  const processes = await within(
    diagnosticsLimit,
    new Promise<string>((resolve) =>
      execFile('ps', ['-eo', 'pid,stat,pcpu,pmem,etime,comm', '--sort=-pcpu'], { timeout: diagnosticsLimit }, (error, stdout) =>
        resolve(error ? `ps failed: ${error.message}` : stdout.split('\n').slice(0, 11).join('\n')),
      ),
    ),
  )
  lines.push('busiest processes:', processes ?? 'ps did not answer')
  return lines.join('\n')
}

/**
 * Runs `work`, one step of a journey on `page`, and fails it after `limit` ms with evidence, where Playwright's own
 * timeouts did not end it: a click once waited for the browser until the 240 second test timeout, with no trace of
 * why. On a hang it attaches a screenshot and a text with the page URL, the console output and page errors since the
 * step began, and the machine state to the test, then throws.
 */
export async function guardStep<T>(
  page: Page,
  testInfo: TestInfo,
  step: string,
  limit: number,
  work: () => Promise<T>,
): Promise<T> {
  const events: string[] = []
  const onConsole = (message: { type(): string; text(): string }) => events.push(`console ${message.type()}: ${message.text()}`)
  const onError = (error: Error) => events.push(`page error: ${error.message}`)
  const onFailed = (request: { method(): string; url(): string; failure(): { errorText: string } | null }) =>
    events.push(`request failed: ${request.method()} ${request.url()} ${request.failure()?.errorText ?? ''}`)
  page.on('console', onConsole)
  page.on('pageerror', onError)
  page.on('requestfailed', onFailed)
  try {
    return await failAfter(step, limit, work, async () => {
      const name = step.replace(/[^a-z0-9]+/gi, '-').toLowerCase()
      const screenshot = await within(diagnosticsLimit, page.screenshot({ timeout: diagnosticsLimit }).catch(() => undefined))
      if (screenshot) await testInfo.attach(`hang-${name}.png`, { body: screenshot, contentType: 'image/png' })
      const report = [
        `page: ${page.isClosed() ? 'closed' : page.url()}`,
        screenshot ? 'screenshot: attached' : 'screenshot: none, the page did not answer',
        'events since the step began:',
        ...(events.length ? events.slice(-50) : ['none']),
        await machineState(),
      ].join('\n')
      await testInfo.attach(`hang-${name}.txt`, { body: report, contentType: 'text/plain' })
      return report
    })
  } finally {
    page.off('console', onConsole)
    page.off('pageerror', onError)
    page.off('requestfailed', onFailed)
  }
}
