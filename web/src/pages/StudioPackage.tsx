import { linter, type Diagnostic as EditorDiagnostic } from '@codemirror/lint'
import type { Extension } from '@codemirror/state'
import { Flex, Modal, Select, Tabs, Typography } from 'antd'
import { useCallback, useContext, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { CodeBlock } from '../platform/CodeBlock'
import { EmptyState } from '../platform/EmptyState'
import { QuotaRefusedError } from '../platform/quota'
import { ReasonDialog } from '../platform/ReasonDialog'
import { ResizeHandle } from '../platform/ResizeHandle'
import { navigationWidth } from '../platform/ShellNavigation'
import { PageActions, PageLayout } from '../platform/PageLayout'
import { StatusTag } from '../platform/StatusTag'
import {
  buildStudioPackage,
  createStudioPackageFile,
  deleteStudioPackageFile,
  fetchStudioLiveDiff,
  fetchStudioPackageFile,
  fetchStudioRequest,
  moveStudioPackageFiles,
  packageManifestFile as manifestFile,
  runStudioPackage,
  saveStudioPackageFile,
  ServerUnreachableError,
  testStudioPackage,
  type StudioDiagnostic,
  type StudioPackageFile,
  type StudioPackageFileItem,
  type StudioPackageTestOutcome,
  type StudioRequest,
} from '../platform/studio'
import { studioPackageAddress } from '../platform/studioAddress'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useFollowJob } from '../platform/useFollowJob'
import CodeEditor, { type CodeEditorHandle } from './CodeEditor'
import { codeLanguage } from './codeLanguage'
import { PlatformContext } from './context'
import { jsonViewHeight } from './jsonViewHeight'
import { PackageExplorer, type ExplorerTarget } from './PackageExplorer'
import { PackageRun, type PackageOperation } from './PackageRun'
import { SaveBar } from './SaveBar'
import { FileAtLine, StudioProblemList } from './StudioProblems'
import { Chevron } from '../platform/Chevron'

/** What the workspace reads from a package's manifest. */
interface PackageManifest {
  label?: string
  version?: string
  language?: string
  adapter?: string
  operations?: PackageOperation[]
}

function readManifest(content: string | undefined): PackageManifest {
  try {
    const parsed: unknown = content === undefined ? {} : JSON.parse(content)
    return parsed !== null && typeof parsed === 'object' ? (parsed as PackageManifest) : {}
  } catch {
    return {}
  }
}

/** A file the author opened: as loaded, as edited, and whether a save found it changed by someone else. */
interface OpenFile {
  loaded: StudioPackageFile
  text: string
  conflict: boolean
  /** Counts the times the file was put back, so its editor starts again with no steps. */
  generation: number
}

/** Whether the package differs from the live release, read from the live diff. */
type PackageStatus = { release: number; changed: boolean } | 'unpublished' | undefined

/** A tool of the workspace that runs on the server, and the request that runs it. */
type Job = 'build' | 'test' | 'run'

/** The dialog that is open, if any. */
type Dialog =
  | { kind: 'newFile'; folder: string }
  | { kind: 'newFolder'; folder: string }
  | { kind: 'rename'; target: ExplorerTarget }
  | { kind: 'delete'; target: ExplorerTarget }
  | { kind: 'close'; path: string }
  | { kind: 'quickOpen' }
  | null

/** The height of the explorer's title row and of the file tabs, so their rules meet in one line. */
const rowHeight = 40
/** The least width of the explorer, so a file's name still reads. */
const explorerLeast = 160
const panelLeast = 120
const panelFirst = 240
const panelTabs = ['problems', 'output', 'tests', 'run'] as const
type PanelTab = (typeof panelTabs)[number]

/** The name of a file or folder: its last segment. */
function nameOf(path: string): string {
  const trimmed = path.endsWith('/') ? path.slice(0, -1) : path
  return trimmed.slice(trimmed.lastIndexOf('/') + 1)
}

/**
 * Proposed: an open package as a place to write, build, test and run it, as a code editor's workspace, see #3978 and
 * docs/reference/studio-packages.md#a-packages-files. The workspace is one frame that fills the window down to the
 * footer: the explorer of files and folders on the left, the open files as tabs with the open file's path above its
 * editor and a status line of the cursor's place, its language and what is unsaved below it, and the output panel under
 * the editor, whose height a handle sets, with Problems, Output, Tests and Run. The heading's Build, Run tests and Run…
 * save every changed file first, then send their request to the package host in the tenant's sandbox, and each opens
 * its tab. A failed test's place opens its file at that line, as a problem's does. Ctrl+S saves every changed
 * file, Ctrl+P opens a file by its name and Ctrl+Shift+B builds. Without the developer role the files are read-only and
 * no save bar, write or run shows.
 */
export default function StudioPackage({
  application,
  packageId,
  file: requestedFile,
  files,
  diagnostics,
  developer,
  busy,
  onChanged,
}: {
  application: string
  packageId: string
  /** The file the address opens, or `null` to open the adapter, else the manifest. */
  file: string | null
  /** The package's files. */
  files: readonly StudioPackageFileItem[]
  /** The folder's diagnostics, of which the package's show in the Problems tab. */
  diagnostics: readonly StudioDiagnostic[]
  developer: boolean
  /** Studio is reconnecting, so nothing is saved. */
  busy: boolean
  /** A write changed the folder, with the diagnostics it answered. */
  onChanged: (diagnostics: StudioDiagnostic[]) => void
}) {
  const t = useText()
  const navigate = useNavigate()
  const { colour, size, spacing, type } = useDesignTokens()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const [open, setOpen] = useState<Record<string, OpenFile>>({})
  const [tabs, setTabs] = useState<string[]>([])
  const [failed, setFailed] = useState<Record<string, true>>({})
  const [steps, setSteps] = useState<Record<string, { undo: number; redo: number }>>({})
  const [cursor, setCursor] = useState<{ line: number; column: number }>({ line: 1, column: 1 })
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [writeError, setWriteError] = useState<string | null>(null)
  const [status, setStatus] = useState<PackageStatus>()
  const [emptyFolders, setEmptyFolders] = useState<string[]>([])
  const [dialog, setDialog] = useState<Dialog>(null)
  const [panelOpen, setPanelOpen] = useState(true)
  const [panelHeight, setPanelHeight] = useState(panelFirst)
  const [explorerWidth, setExplorerWidth] = useState(navigationWidth)
  const [frameWidth, setFrameWidth] = useState(960)
  const explorer = useRef<HTMLDivElement>(null)
  const [panelTab, setPanelTab] = useState<PanelTab>('problems')
  const [requests, setRequests] = useState<Partial<Record<Job, StudioRequest>>>({})
  const [lastJob, setLastJob] = useState<Job | null>(null)
  const [jobError, setJobError] = useState<string | null>(null)
  const [frameHeight, setFrameHeight] = useState(480)
  const frame = useRef<HTMLDivElement>(null)
  const panel = useRef<HTMLElement>(null)
  const editors = useRef(new Map<string, CodeEditorHandle | null>())
  const pendingGoTo = useRef<{ path: string; line: number; column?: number } | null>(null)

  const paths = useMemo(() => files.map((item) => item.path), [files])
  const manifest = readManifest(open[manifestFile]?.text)
  const file =
    requestedFile ??
    (manifest.adapter !== undefined && paths.includes(manifest.adapter) ? manifest.adapter : manifestFile)
  const known = paths.includes(file)
  const packagePrefix = `packages/${packageId}/`
  const buildProblems = requests.build?.status === 'running' ? [] : (requests.build?.diagnostics ?? [])
  const problems = [
    ...diagnostics.filter((diagnostic) => diagnostic.file.startsWith(packagePrefix)),
    ...buildProblems.filter(
      (built) =>
        !diagnostics.some(
          (diagnostic) =>
            diagnostic.code === built.code && diagnostic.file === built.file && diagnostic.message === built.message,
        ),
    ),
  ]
  const dirty = Object.entries(open)
    .filter(([, entry]) => entry.text !== entry.loaded.content)
    .map(([path]) => path)
  const conflicts = Object.entries(open)
    .filter(([, entry]) => entry.conflict)
    .map(([path]) => path)
  // A problem at a line of a file marks that line in the file's editor, as a code editor's checks do.
  const problemKey = JSON.stringify(problems.filter((problem) => problem.line !== undefined))
  const checks = useMemo(() => {
    const placed = (JSON.parse(problemKey) as StudioDiagnostic[]).filter((problem) => problem.line !== undefined)
    const byFile = new Map<string, Extension>()
    for (const path of new Set(placed.map((problem) => problem.file.slice(packagePrefix.length)))) {
      const own = placed.filter((problem) => problem.file === `${packagePrefix}${path}`)
      byFile.set(
        path,
        linter(
          (view): EditorDiagnostic[] =>
            own.map((problem) => {
              const line = view.state.doc.line(Math.min(Math.max(problem.line!, 1), view.state.doc.lines))
              const from = Math.min(line.from + Math.max((problem.column ?? 1) - 1, 0), line.to)
              return {
                from,
                to: Math.max(from, line.to),
                severity: problem.severity,
                message: `${problem.code}: ${problem.message}`,
              }
            }),
          { delay: 0 },
        ),
      )
    }
    return byFile
  }, [problemKey, packagePrefix])

  // The open file joins the tabs, after the tab that was open, and a folder that now holds a file is kept by the file.
  const openTabs = known && !tabs.includes(file) ? [...tabs, file] : tabs
  const shownEmptyFolders = emptyFolders.filter((folder) => !paths.some((path) => path.startsWith(folder)))
  const running = (job: Job) => {
    const request = requests[job]
    return request !== undefined && (request.status === 'queued' || request.status === 'running')
  }

  // The manifest is open from the start, as the heading reads it, and every tab's file loads once, when it opens.
  const wanted = [...new Set([manifestFile, ...openTabs])].filter((path) => paths.includes(path))
  const missing = wanted.filter((path) => open[path] === undefined && failed[path] === undefined).join('\n')
  useEffect(() => {
    if (missing === '') {
      return
    }
    const controller = new AbortController()
    for (const path of missing.split('\n')) {
      loadFile(application, packageId, path, controller.signal).then(
        (loaded) =>
          setOpen((current) => ({
            ...current,
            [path]: { loaded, text: loaded.content, conflict: false, generation: 0 },
          })),
        () => {
          if (!controller.signal.aborted) {
            setFailed((current) => ({ ...current, [path]: true }))
          }
        },
      )
    }
    return () => controller.abort()
  }, [application, packageId, missing])

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioLiveDiff(application, controller.signal).then(
      (diff) =>
        setStatus(
          diff === null
            ? 'unpublished'
            : { release: diff.release, changed: diff.resources.some((entry) => entry.file.startsWith(packagePrefix)) },
        ),
      () => {
        if (!controller.signal.aborted) {
          setStatus(undefined)
        }
      },
    )
    return () => controller.abort()
  }, [application, packagePrefix, files])

  // The frame fills the window down to the footer, as the code editor of a resource does.
  const fit = useCallback(() => {
    const element = frame.current
    if (!element) {
      return
    }
    // The workspace takes the page's padding under it, so it ends on the footer's rule. The shell is as tall as the window,
    // or as the design system's frame that sets `--shell-min-height`, so the workspace measures from the shell's top.
    const framed = parseFloat(getComputedStyle(element).getPropertyValue('--shell-min-height'))
    let shellRoot: Element = element
    for (
      let outer = element.parentElement?.closest('.ant-layout');
      outer;
      outer = outer.parentElement?.closest('.ant-layout')
    ) {
      shellRoot = outer
    }
    const inFrame = Number.isFinite(framed)
    const next = jsonViewHeight({
      windowHeight: inFrame ? framed : window.innerHeight,
      top: element.getBoundingClientRect().top - (inFrame ? shellRoot.getBoundingClientRect().top : -window.scrollY),
      footerHeight: shellRoot.querySelector('footer')?.getBoundingClientRect().height ?? 0,
      below: 0,
      gap: 0,
    })
    setFrameHeight((current) => (current === next ? current : Math.max(next, 420)))
    const width = Math.round(element.getBoundingClientRect().width)
    setFrameWidth((current) => (current === width ? current : width))
  }, [])
  useLayoutEffect(fit)
  useEffect(() => {
    window.addEventListener('resize', fit)
    return () => window.removeEventListener('resize', fit)
  }, [fit])

  // A problem chosen in another file opens that file, then moves its editor to the problem once the editor is there.
  const loadedFile = open[file] !== undefined
  useEffect(() => {
    const pending = pendingGoTo.current
    const handle = editors.current.get(file)
    if (pending !== null && pending.path === file && loadedFile && handle) {
      handle.goTo(pending.line, pending.column)
      pendingGoTo.current = null
    }
  }, [file, loadedFile])

  // Opening a file keeps the tab that was open and adds the file's own.
  const openFile = (path: string) => {
    setTabs(openTabs.includes(path) ? openTabs : [...openTabs, path])
    navigate(studioPackageAddress(application, packageId, path))
  }

  function edit(path: string, text: string) {
    setOpen((current) => ({ ...current, [path]: { ...current[path], text } }))
  }

  /** Puts each file back as loaded, or as the server now holds it, with a new editor so no step undoes into the change. */
  async function putBack(list: readonly string[], reload: boolean) {
    for (const path of list) {
      const loaded = reload ? await loadFile(application, packageId, path) : open[path].loaded
      setOpen((current) => ({
        ...current,
        [path]: { loaded, text: loaded.content, conflict: false, generation: current[path].generation + 1 },
      }))
      setSteps((current) => ({ ...current, [path]: { undo: 0, redo: 0 } }))
    }
  }

  /** Saves every changed file, and answers whether all of them are saved. */
  async function saveAll(): Promise<boolean> {
    if (dirty.length === 0) {
      return true
    }
    if (!developer || saving) {
      return false
    }
    let saved = true
    setSaving(true)
    setSaveError(null)
    let answered: StudioDiagnostic[] | null = null
    try {
      for (const path of dirty) {
        const entry = open[path]
        if (entry.conflict) {
          saved = false
          continue
        }
        const result = await saveStudioPackageFile(application, packageId, path, entry.loaded.version, entry.text)
        if (result.status === 'conflict') {
          setOpen((current) => ({ ...current, [path]: { ...current[path], conflict: true } }))
          saved = false
          continue
        }
        answered = result.diagnostics
        setOpen((current) => ({
          ...current,
          [path]: {
            ...current[path],
            loaded: { ...current[path].loaded, version: result.version, content: entry.text },
          },
        }))
      }
      if (answered !== null) {
        onChanged(answered)
      }
    } catch (error) {
      setSaveError(error instanceof QuotaRefusedError ? error.message : t('shell.studio.package.saveFailed'))
      saved = false
    } finally {
      setSaving(false)
    }
    return saved
  }

  /** Closes a tab, and opens the tab beside it when it was the open one. */
  function closeTab(path: string) {
    const index = openTabs.indexOf(path)
    const rest = openTabs.filter((tab) => tab !== path)
    setTabs(rest)
    if (dirty.includes(path)) {
      void putBack([path], false)
    }
    if (path === file) {
      const next = rest[Math.min(index, rest.length - 1)]
      navigate(studioPackageAddress(application, packageId, next ?? manifestFile))
    }
  }

  /** The files a target names: the file, or every file in the folder. */
  const filesOf = (target: ExplorerTarget) =>
    target.kind === 'file' ? [target.path] : paths.filter((path) => path.startsWith(target.path))

  async function versionsOf(list: readonly string[]): Promise<Record<string, string>> {
    const entries = await Promise.all(
      list.map(async (path) => [
        path,
        open[path]?.loaded.version ?? (await loadFile(application, packageId, path)).version,
      ]),
    )
    return Object.fromEntries(entries)
  }

  /** Moves a file or folder to `to`, a file's path or a folder's path with a trailing slash, as one revision. */
  async function move(target: ExplorerTarget, to: string): Promise<boolean> {
    setWriteError(null)
    const list = filesOf(target)
    if (list.length === 0) {
      // A folder that holds no file yet is only in the explorer.
      setEmptyFolders((current) => [...current.filter((folder) => folder !== target.path), to])
      return true
    }
    const versions = await versionsOf(list)
    const moves = list.map((path) => ({
      from: path,
      to: target.kind === 'file' ? to : `${to}${path.slice(target.path.length)}`,
      version: versions[path],
    }))
    const result = await moveStudioPackageFiles(application, packageId, moves)
    if (result.status !== 'moved') {
      setWriteError(
        t(result.status === 'conflict' ? 'shell.studio.package.moveConflict' : 'shell.studio.package.moveRefused'),
      )
      return false
    }
    const renamed = new Map(moves.map((entry) => [entry.from, entry.to]))
    setTabs(openTabs.map((tab) => renamed.get(tab) ?? tab))
    setOpen((current) =>
      Object.fromEntries(Object.entries(current).map(([path, entry]) => [renamed.get(path) ?? path, entry])),
    )
    onChanged(result.diagnostics)
    const moved = renamed.get(file)
    if (moved !== undefined) {
      openFile(moved)
    }
    return true
  }

  async function remove(target: ExplorerTarget): Promise<boolean> {
    setWriteError(null)
    const list = filesOf(target)
    if (list.length === 0) {
      setEmptyFolders((current) => current.filter((folder) => !folder.startsWith(target.path)))
      return true
    }
    const versions = await versionsOf(list)
    let answered: StudioDiagnostic[] | null = null
    for (const path of list) {
      const result = await deleteStudioPackageFile(application, packageId, path, versions[path])
      if (result.status === 'conflict') {
        setWriteError(t('shell.studio.package.moveConflict'))
        break
      }
      answered = result.diagnostics
    }
    setTabs(openTabs.filter((tab) => !list.includes(tab)))
    setOpen((current) => Object.fromEntries(Object.entries(current).filter(([path]) => !list.includes(path))))
    if (answered !== null) {
      onChanged(answered)
    }
    if (list.includes(file)) {
      navigate(studioPackageAddress(application, packageId))
    }
    return true
  }

  // A request of the workspace is followed until it ends, as a publish is.
  const followed = (Object.entries(requests) as [Job, StudioRequest][]).find(
    ([, request]) => request.status === 'queued' || request.status === 'running',
  )
  useFollowJob(followed ? `${application}/${followed[1].id}` : null, {
    read: (signal) => fetchStudioRequest(application, followed![1].id, signal),
    ends: (next) => next.status !== 'queued' && next.status !== 'running',
    interval: 1000,
    waitFirst: true,
    onAnswer: (next) => setRequests((current) => ({ ...current, [followed![0]]: next })),
    onEnd: (next) => setRequests((current) => ({ ...current, [followed![0]]: next })),
    onError: (error) => {
      if (!(error instanceof ServerUnreachableError)) {
        setJobError(t('shell.studio.package.jobFailed'))
      }
      setRequests((current) => {
        const rest = { ...current }
        delete rest[followed![0]]
        return rest
      })
      return 'stop'
    },
  })

  async function start(job: Job, send: () => Promise<StudioRequest>) {
    setJobError(null)
    // Proposed, see #3978: a build, a test or a run takes the files as the editor shows them, so it saves every changed
    // file first, as a code editor saves before it runs a task. A file it cannot save stops it, with that file's notice.
    if (!(await saveAll())) {
      return
    }
    setLastJob(job)
    setPanelOpen(true)
    setPanelTab(job === 'build' ? 'output' : job === 'test' ? 'tests' : 'run')
    try {
      const request = await send()
      setRequests((current) => ({ ...current, [job]: request }))
    } catch (error) {
      setJobError(error instanceof QuotaRefusedError ? error.message : t('shell.studio.package.jobFailed'))
    }
  }
  const build = () => void start('build', () => buildStudioPackage(application, packageId))
  const runTests = () => void start('test', () => testStudioPackage(application, packageId))

  function onKeyDown(event: React.KeyboardEvent) {
    const mod = event.ctrlKey || event.metaKey
    if (mod && event.key.toLowerCase() === 's') {
      event.preventDefault()
      void saveAll()
    } else if (mod && event.key.toLowerCase() === 'p') {
      event.preventDefault()
      setDialog({ kind: 'quickOpen' })
    } else if (mod && event.shiftKey && event.key.toLowerCase() === 'b' && developer && !running('build')) {
      event.preventDefault()
      build()
    }
  }

  const languageName = manifest.language ?? 'csharp'
  const line =
    manifest.version === undefined
      ? null
      : t('shell.studio.package.line')
          .replace('{language}', t(`shell.studio.package.language.${languageName}`))
          .replace('{version}', manifest.version)
  const statusText =
    status === undefined
      ? null
      : status === 'unpublished'
        ? t('shell.studio.band.notPublished')
        : t(status.changed ? 'shell.studio.package.status.changed' : 'shell.studio.package.status.unchanged').replace(
            '{release}',
            String(status.release),
          )

  const activeSteps = steps[file] ?? { undo: 0, redo: 0 }
  const panelMax = Math.max(panelLeast, frameHeight - 160)
  const explorerMost = () => Math.max(explorerLeast, Math.floor(frameWidth / 2))
  const lastRequest = lastJob === null ? undefined : requests[lastJob]
  const tests = requests.test?.packageTests ?? null
  const failedTests = tests?.filter((test) => test.outcome === 'failed').length ?? 0
  const testColumns: AxisColumn<StudioPackageTestOutcome>[] = [
    {
      key: 'outcome',
      kind: 'fixed',
      title: t('shell.studio.package.tests.outcome'),
      render: (_value, test) => (
        <StatusTag tone={test.outcome === 'passed' ? 'success' : 'danger'}>
          {t(`shell.studio.package.tests.${test.outcome}`)}
        </StatusTag>
      ),
    },
    { key: 'name', kind: 'freeText', title: t('shell.studio.package.tests.name'), dataIndex: 'name' },
    {
      key: 'duration',
      kind: 'fixed',
      align: 'right',
      title: t('shell.studio.package.tests.duration'),
      render: (_value, test) =>
        test.durationMs === undefined
          ? null
          : t('shell.studio.package.run.milliseconds').replace('{count}', String(test.durationMs)),
    },
    {
      key: 'message',
      kind: 'freeText',
      title: t('shell.studio.package.tests.message'),
      // Proposed, see #3978: a failed test's place follows its message and opens its file at that line.
      render: (_value, test) => {
        const file = test.file
        if (file === undefined) {
          return test.message
        }
        return (
          <Flex wrap align="baseline" style={{ columnGap: spacing.md }}>
            {test.message}
            <FileAtLine file={file} line={test.line} onOpen={() => goTo(file, test.line, test.column)} />
          </Flex>
        )
      },
    },
  ]
  const fileName = (diagnostic: StudioDiagnostic) => diagnostic.file.slice(packagePrefix.length)
  /** Opens a file of the package, at a line when one is given. */
  const goTo = (path: string, line?: number, column?: number) => {
    if (line !== undefined) {
      pendingGoTo.current = { path, line, column }
    }
    openFile(path)
  }
  const goToProblem = (diagnostic: StudioDiagnostic) => goTo(fileName(diagnostic), diagnostic.line, diagnostic.column)
  // A problem in a file marks its line in the editor, as a code editor's checks do.

  return (
    <div data-testid="studio-package" onKeyDown={onKeyDown}>
      <PageLayout
        title={manifest.label ?? packageId}
        line={line}
        tags={statusText === null ? undefined : <StatusTag tone="neutral">{statusText}</StatusTag>}
      >
        <PageActions>
          <Flex gap="small" wrap>
            <AxisButton
              role="secondary"
              data-testid="package-build"
              aria-keyshortcuts="Control+Shift+B"
              disabled={!developer || busy}
              loading={running('build')}
              onClick={build}
            >
              {t('shell.studio.package.build')}
            </AxisButton>
            <AxisButton
              role="secondary"
              data-testid="package-test"
              disabled={!developer || busy}
              loading={running('test')}
              onClick={runTests}
            >
              {t('shell.studio.package.runTests')}
            </AxisButton>
            <AxisButton
              role="secondary"
              data-testid="package-open-run"
              disabled={!developer || busy}
              onClick={() => {
                setPanelOpen(true)
                setPanelTab('run')
              }}
            >
              {t('shell.studio.package.runOperation')}
            </AxisButton>
          </Flex>
        </PageActions>
        {!developer && (
          <AxisAlert data-testid="studio-package-read-only" kind="info" message={t('shell.studio.package.readOnly')} />
        )}
        {conflicts.map((path) => (
          <AxisAlert
            key={path}
            data-testid="studio-conflict"
            kind="warning"
            message={t('shell.studio.package.conflict').replace('{file}', path)}
            action={
              <AxisButton role="secondary" onClick={() => void putBack([path], true)}>
                {t('shell.studio.reload')}
              </AxisButton>
            }
          />
        ))}
        {[saveError, writeError, jobError]
          .filter((message): message is string => message !== null)
          .map((message) => (
            <AxisAlert key={message} kind="error" message={message} />
          ))}
        <div
          ref={frame}
          data-testid="package-workspace"
          style={{
            display: 'flex',
            height: frameHeight,
            // The workspace reaches the page's edges and the footer, as the band does, and rules part its regions, as
            // the header and the sidebar part the shell.
            margin: `0 -${spacing.md}px -${spacing.md}px`,
            borderTop: `1px solid ${colour.borderSubtle}`,
            overflow: 'hidden',
          }}
        >
          <div
            ref={explorer}
            style={{
              position: 'relative',
              width: explorerWidth,
              flex: 'none',
              borderInlineEnd: `1px solid ${colour.borderSubtle}`,
            }}
          >
            {/* Proposed, see #3978: the explorer's width is set by a handle on its rule, from `explorerLeast` to half the
              workspace, as the output panel's height is. */}
            <ResizeHandle
              edge="end"
              labelKey="shell.studio.package.resizeFiles"
              size={explorerWidth}
              min={explorerLeast}
              max={explorerMost()}
              measure={(event) =>
                explorer.current ? event.clientX - explorer.current.getBoundingClientRect().left : explorerWidth
              }
              onChange={(next) => setExplorerWidth(Math.min(explorerMost(), Math.max(explorerLeast, Math.round(next))))}
            />
            <PackageExplorer
              paths={paths}
              emptyFolders={shownEmptyFolders}
              dirty={dirty}
              active={file}
              developer={developer}
              locale={locale}
              rowHeight={rowHeight}
              actions={{
                onOpen: openFile,
                onNewFile: (folder) => setDialog({ kind: 'newFile', folder }),
                onNewFolder: (folder) => setDialog({ kind: 'newFolder', folder }),
                onRename: (target) => setDialog({ kind: 'rename', target }),
                onDelete: (target) => setDialog({ kind: 'delete', target }),
                onMove: (target, folder) =>
                  void move(
                    target,
                    target.kind === 'file' ? `${folder}${nameOf(target.path)}` : `${folder}${nameOf(target.path)}/`,
                  ),
                onCopyPath: (target) => void navigator.clipboard?.writeText(`${packagePrefix}${target.path}`),
                onRefresh: () => onChanged([...diagnostics]),
              }}
            />
          </div>
          <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}>
            {/* The file tabs read as the panel's tabs do: no box round a tab, the open one underlined in `primary`, and one rule
              under the row, level with the explorer's. */}
            {/* Proposed, see #3978: the file tabs and the panel's tabs are one row style: as high as the explorer's title row,
              the same gap between every tab, no box round a tab, the open one underlined in `primary`, and one rule under
              the row. */}
            <style>{`.axis-workspace-tabs > .ant-tabs-nav { margin: 0 !important; height: ${rowHeight}px; padding-inline: ${spacing.sm}px; } .axis-workspace-tabs > .ant-tabs-nav::before { border-bottom-color: ${colour.borderSubtle}; } .axis-workspace-tabs .ant-tabs-tab, .axis-workspace-tabs .ant-tabs-tab + .ant-tabs-tab { border: none !important; background: transparent !important; border-radius: 0 !important; margin: 0 !important; padding: 0 ${spacing.sm}px !important; height: ${rowHeight}px; font-size: ${type.fontSize}px; } .axis-workspace-tabs .ant-tabs-ink-bar { display: none; } .axis-workspace-tabs .ant-tabs-tab-active { box-shadow: inset 0 -2px 0 ${colour.primary}; } .axis-file-tabs .ant-tabs-body-holder { display: none; }`}</style>
            <Tabs
              className="axis-workspace-tabs axis-file-tabs"
              type="editable-card"
              hideAdd
              activeKey={file}
              onChange={openFile}
              onEdit={(key, action) => {
                if (action === 'remove') {
                  const path = String(key)
                  if (dirty.includes(path)) {
                    setDialog({ kind: 'close', path })
                  } else {
                    closeTab(path)
                  }
                }
              }}
              items={openTabs.map((path) => ({
                key: path,
                label: (
                  <span title={path} data-testid={`package-tab-${path}`}>
                    {nameOf(path)}
                    {dirty.includes(path) && (
                      <span
                        role="img"
                        aria-label={t('shell.studio.package.unsaved')}
                        style={{
                          display: 'inline-block',
                          width: 6,
                          height: 6,
                          marginInlineStart: spacing.xs,
                          borderRadius: '50%',
                          background: colour.primary,
                          verticalAlign: 'middle',
                        }}
                      />
                    )}
                  </span>
                ),
              }))}
            />
            <div style={{ flex: 1, minHeight: 0, position: 'relative' }}>
              {!known ? (
                <div style={{ padding: spacing.sm }}>
                  <AxisAlert kind="warning" message={t('shell.studio.package.notFound')} />
                </div>
              ) : failed[file] ? (
                <div style={{ padding: spacing.sm }}>
                  <AxisAlert kind="error" message={t('shell.studio.package.loadFailed')} />
                </div>
              ) : null}
              {Object.entries(open)
                .filter(([path]) => openTabs.includes(path))
                .map(([path, entry]) => (
                  // Each open file keeps its editor, hidden while another file shows, so its steps live on.
                  <div key={path} style={{ display: path === file ? undefined : 'none', height: '100%' }}>
                    <CodeEditor
                      key={entry.generation}
                      ref={(handle) => {
                        editors.current.set(path, handle)
                      }}
                      label={path}
                      value={entry.text}
                      onChange={(text) => edit(path, text)}
                      language={codeLanguage(path)}
                      checks={checks.get(path)}
                      readOnly={!developer}
                      fill
                      findTexts={{
                        field: t('shell.studio.package.findField'),
                        count: t('shell.studio.json.findCount'),
                        none: t('shell.studio.json.findNone'),
                      }}
                      onHistory={(next) => setSteps((all) => ({ ...all, [path]: next }))}
                      onCursor={(next) => path === file && setCursor(next)}
                    />
                  </div>
                ))}
            </div>
            <section
              ref={panel}
              aria-label={t('shell.studio.package.panel')}
              data-testid="studio-package-panel"
              style={{
                position: 'relative',
                flex: 'none',
                borderTop: `1px solid ${colour.borderSubtle}`,
                height: panelOpen ? panelHeight : undefined,
                display: 'flex',
                flexDirection: 'column',
              }}
            >
              {panelOpen && (
                <ResizeHandle
                  edge="top"
                  labelKey="shell.studio.package.resizePanel"
                  size={panelHeight}
                  min={panelLeast}
                  max={panelMax}
                  measure={(event) =>
                    panel.current ? panel.current.getBoundingClientRect().bottom - event.clientY : panelHeight
                  }
                  onChange={(next) => setPanelHeight(Math.min(panelMax, Math.max(panelLeast, Math.round(next))))}
                />
              )}
              {/* The panel's tabs fill it, so the part under the tab bar scrolls inside the panel. */}
              <style>{`.axis-package-panel { display: flex; flex-direction: column; } .axis-package-panel > .ant-tabs-body-holder { flex: 1; min-height: 0; } .axis-package-panel > .ant-tabs-body-holder > .ant-tabs-body, .axis-package-panel .ant-tabs-content-active { height: 100%; }`}</style>
              <Tabs
                activeKey={panelTab}
                onChange={(key) => {
                  setPanelTab(key as PanelTab)
                  setPanelOpen(true)
                }}
                className="axis-workspace-tabs axis-package-panel"
                style={{ flex: 1, minHeight: 0 }}
                tabBarExtraContent={
                  <AxisButton
                    role="icon"
                    size="small"
                    nameKey={panelOpen ? 'shell.studio.package.panel.hide' : 'shell.studio.package.panel.show'}
                    icon={<Chevron direction={panelOpen ? 'down' : 'up'} />}
                    aria-expanded={panelOpen}
                    onClick={() => setPanelOpen((value) => !value)}
                  />
                }
                items={[
                  {
                    key: 'problems',
                    label: t('shell.studio.package.panel.problems').replace('{count}', String(problems.length)),
                    children: panelOpen && (
                      <PanelBody>
                        {problems.length === 0 ? (
                          <EmptyState text={t('shell.studio.problems.none')} />
                        ) : (
                          <StudioProblemList
                            diagnostics={problems}
                            onOpenFile={{ name: fileName, open: goToProblem }}
                          />
                        )}
                      </PanelBody>
                    ),
                  },
                  {
                    key: 'output',
                    label: t('shell.studio.package.panel.output'),
                    children: panelOpen && (
                      <PanelBody>
                        {lastRequest === undefined ? (
                          <EmptyState text={t('shell.studio.package.panel.outputNone')} />
                        ) : (
                          <Flex vertical gap="small" data-testid="package-output">
                            <Flex gap="small" align="center">
                              <Typography.Text strong>{t(`shell.studio.package.job.${lastJob}`)}</Typography.Text>
                              <StatusTag tone={requestTone(lastRequest)}>
                                {t(`shell.studio.package.jobState.${lastRequest.status}`)}
                              </StatusTag>
                            </Flex>
                            <CodeBlock plain text={(lastRequest.output ?? []).join('\n') || null} />
                          </Flex>
                        )}
                      </PanelBody>
                    ),
                  },
                  {
                    key: 'tests',
                    label:
                      tests === null
                        ? t('shell.studio.package.panel.tests')
                        : t('shell.studio.package.panel.testsCount')
                            .replace('{passed}', String(tests.length - failedTests))
                            .replace('{failed}', String(failedTests)),
                    children: panelOpen && (
                      <PanelBody>
                        {running('test') ? (
                          <Typography.Text type="secondary">{t('shell.studio.package.tests.running')}</Typography.Text>
                        ) : tests === null ? (
                          <EmptyState text={t('shell.studio.package.panel.testsNone')} />
                        ) : (
                          <AxisTable<StudioPackageTestOutcome>
                            compact
                            rowKey={(test) => test.name}
                            columns={testColumns}
                            dataSource={tests}
                            testId="package-tests"
                          />
                        )}
                      </PanelBody>
                    ),
                  },
                  {
                    key: 'run',
                    label: t('shell.studio.package.panel.run'),
                    children: panelOpen && (
                      <PackageRun
                        application={application}
                        packageId={packageId}
                        operations={manifest.operations ?? []}
                        developer={developer}
                        locale={locale}
                        running={running('run')}
                        run={requests.run?.run ?? null}
                        output={requests.run?.output ?? []}
                        rowHeight={rowHeight}
                        onRun={(operation, input) =>
                          void start('run', () => runStudioPackage(application, packageId, operation, input))
                        }
                      />
                    ),
                  },
                ]}
              />
            </section>
            <Flex
              align="center"
              gap="middle"
              data-testid="package-status-line"
              style={{
                flex: 'none',
                minHeight: 24,
                paddingInline: spacing.md,
                borderTop: `1px solid ${colour.borderSubtle}`,
                color: colour.textMuted,
                fontSize: type.fontSize - 2,
              }}
            >
              <span>
                {t('shell.studio.package.cursor')
                  .replace('{line}', String(cursor.line))
                  .replace('{column}', String(cursor.column))}
              </span>
              <span>{languageLabel(file, t)}</span>
              <span style={{ marginInlineStart: 'auto' }}>
                {dirty.length === 0
                  ? t('shell.studio.package.allSaved')
                  : t(
                      dirty.length === 1 ? 'shell.studio.package.unsavedOne' : 'shell.studio.package.unsavedMany',
                    ).replace('{count}', String(dirty.length))}
              </span>
            </Flex>
          </div>
        </div>
        {developer && (
          <SaveBar
            unsaved={dirty.length > 0}
            undoable={activeSteps.undo > 0}
            redoable={activeSteps.redo > 0}
            saveable={dirty.some((path) => !open[path].conflict) && !saving && !busy}
            busy={saving}
            onUndo={() => editors.current.get(file)?.undo()}
            onRedo={() => editors.current.get(file)?.redo()}
            onDiscard={() => void putBack(dirty, false)}
            onSave={() => void saveAll()}
          />
        )}
        {dialog?.kind === 'newFile' && (
          <ReasonDialog
            title={t('shell.studio.package.newFile.title')}
            label={t('shell.studio.package.newFile.label')}
            help={t('shell.studio.package.newFile.help')}
            okText={t('shell.studio.package.newFile.create')}
            cancelText={t('shell.studio.cancel')}
            requiredMessage={t('shell.studio.package.newFile.required')}
            failedText={t('shell.studio.package.saveFailed')}
            maxLength={200}
            initialValue={dialog.folder}
            onClose={() => setDialog(null)}
            onChanged={() => {}}
            onSend={async (path) => {
              const trimmed = path.trim()
              const result = await createStudioPackageFile(application, packageId, trimmed, '')
              if (result.status === 'created') {
                onChanged(result.diagnostics)
                openFile(trimmed)
                return { ok: true }
              }
              const title = t(
                result.status === 'conflict'
                  ? 'shell.studio.package.newFile.exists'
                  : 'shell.studio.package.newFile.refused',
              )
              return { ok: false, problem: { status: result.status === 'conflict' ? 409 : 400, title, errors: {} } }
            }}
          />
        )}
        {dialog?.kind === 'newFolder' && (
          <ReasonDialog
            title={t('shell.studio.package.newFolder.title')}
            label={t('shell.studio.package.newFolder.label')}
            help={t('shell.studio.package.newFolder.help')}
            okText={t('shell.studio.package.newFile.create')}
            cancelText={t('shell.studio.cancel')}
            requiredMessage={t('shell.studio.package.newFolder.required')}
            maxLength={200}
            initialValue={dialog.folder}
            onClose={() => setDialog(null)}
            onChanged={() => {}}
            onSend={async (path) => {
              const folder = `${path.trim().replace(/\/+$/, '')}/`
              setEmptyFolders((current) => (current.includes(folder) ? current : [...current, folder]))
              return { ok: true }
            }}
          />
        )}
        {dialog?.kind === 'rename' && (
          <ReasonDialog
            title={t('shell.studio.package.rename.title').replace('{name}', nameOf(dialog.target.path))}
            label={t('shell.studio.package.rename.label')}
            help={t('shell.studio.package.rename.help')}
            okText={t('shell.studio.package.rename')}
            cancelText={t('shell.studio.cancel')}
            requiredMessage={t('shell.studio.package.newFile.required')}
            failedText={t('shell.studio.package.saveFailed')}
            maxLength={200}
            initialValue={dialog.target.path.replace(/\/$/, '')}
            onClose={() => setDialog(null)}
            onChanged={() => {}}
            onSend={async (next) => {
              const trimmed = next.trim().replace(/\/+$/, '')
              const to = dialog.target.kind === 'folder' ? `${trimmed}/` : trimmed
              const done = await move(dialog.target, to)
              return done
                ? { ok: true }
                : { ok: false, problem: { status: 409, title: t('shell.studio.package.moveConflict'), errors: {} } }
            }}
          />
        )}
        <Modal
          open={dialog?.kind === 'delete'}
          width={size.dialog.small}
          title={
            dialog?.kind === 'delete'
              ? t(
                  dialog.target.kind === 'file'
                    ? 'shell.studio.package.deleteFile.title'
                    : 'shell.studio.package.deleteFolder.title',
                )
                  .replace('{file}', dialog.target.path)
                  .replace('{count}', String(filesOf(dialog.target).length))
              : ''
          }
          okText={t('shell.studio.delete')}
          okButtonProps={{ danger: true }}
          cancelText={t('shell.studio.cancel')}
          onOk={() => {
            if (dialog?.kind === 'delete') {
              void remove(dialog.target).then(() => setDialog(null))
            }
          }}
          onCancel={() => setDialog(null)}
        >
          <span>{t('shell.studio.package.deleteFile.body')}</span>
        </Modal>
        <Modal
          open={dialog?.kind === 'close'}
          width={size.dialog.small}
          title={
            dialog?.kind === 'close'
              ? t('shell.studio.package.closeTab.title').replace('{file}', nameOf(dialog.path))
              : ''
          }
          okText={t('shell.studio.package.closeTab.discard')}
          okButtonProps={{ danger: true }}
          cancelText={t('shell.studio.cancel')}
          onOk={() => {
            if (dialog?.kind === 'close') {
              closeTab(dialog.path)
            }
            setDialog(null)
          }}
          onCancel={() => setDialog(null)}
        >
          <span>{t('shell.studio.package.closeTab.body')}</span>
        </Modal>
        <Modal
          open={dialog?.kind === 'quickOpen'}
          width={size.dialog.small}
          title={t('shell.studio.package.quickOpen')}
          footer={null}
          onCancel={() => setDialog(null)}
          destroyOnHidden
        >
          <Select
            autoFocus
            showSearch
            open
            aria-label={t('shell.studio.package.quickOpen')}
            style={{ width: '100%' }}
            options={paths.map((path) => ({ value: path, label: path }))}
            onChange={(path: string) => {
              setDialog(null)
              openFile(path)
            }}
          />
        </Modal>
      </PageLayout>
    </div>
  )
}

/** The part of the output panel under its tabs, which scrolls on its own and stops at its end. */
function PanelBody({ children }: { children: React.ReactNode }) {
  const { spacing } = useDesignTokens()
  return (
    <div
      style={{
        height: '100%',
        overflowY: 'auto',
        overscrollBehavior: 'contain',
        padding: `${spacing.sm}px ${spacing.md}px`,
      }}
    >
      {children}
    </div>
  )
}

/** What a request's state asks of the reader, as its tag's tone. */
function requestTone(request: StudioRequest): 'neutral' | 'success' | 'danger' {
  return request.status === 'succeeded' ? 'success' : request.status === 'failed' ? 'danger' : 'neutral'
}

/** The language a file's name says, in words, for the status line. */
function languageLabel(path: string, t: (key: string) => string): string {
  const extension = path.slice(path.lastIndexOf('.') + 1).toLowerCase()
  const known: Record<string, string> = {
    cs: 'C#',
    csproj: 'XML',
    ts: 'TypeScript',
    js: 'JavaScript',
    mjs: 'JavaScript',
    py: 'Python',
    json: 'JSON',
    txt: t('shell.studio.package.plainText'),
  }
  return known[extension] ?? t('shell.studio.package.plainText')
}

/** Loads a package file with its text. A file that is gone throws, as any other failure does. */
async function loadFile(
  application: string,
  packageId: string,
  path: string,
  signal?: AbortSignal,
): Promise<StudioPackageFile> {
  const loaded = await fetchStudioPackageFile(application, packageId, path, signal)
  if (loaded === null) {
    throw new Error(`The package file '${path}' is gone.`)
  }
  return { package: packageId, path, ...loaded }
}
