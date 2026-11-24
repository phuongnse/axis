import { Flex, Typography } from 'antd'
import { createContext, useContext, useLayoutEffect, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ContentInsetContext } from './contentInsetContext'
import { useDesignTokens } from './themeMode'

/** The element of the page's heading that {@link PageActions} draws into: `null` before it is drawn, `undefined` outside a page. */
const ActionsSlot = createContext<HTMLElement | null | undefined>(undefined)

/** The name and line that a widget inside the page gives the heading in place of the page's title, see {@link PageName}. */
interface NamedPage {
  name: string
  line: string | null
}

/** Sets the name and line of the heading, or clears them with `null`: `undefined` outside a page. */
const NameSlot = createContext<((named: NamedPage | null) => void) | undefined>(undefined)

/**
 * The layout of a page, see docs/reference/shell.md#page-header: its title as the page's level-one heading, drawn at the
 * size of a level-four one, with its `tags` beside it and its `line`, the kind of the thing, under it in `textMuted`,
 * and the page's sections stacked below. The page's own actions sit on the heading's right, which any component inside
 * the page fills through {@link PageActions}. A widget inside the page may name the heading through {@link PageName}, see
 * docs/reference/shell.md#page-header. With `band`, the heading sits alone in a band on the surface colour that
 * spans the page area, see docs/reference/shell.md#page-header. It replaces the old page container. Pages add no
 * spacing or styling of their own.
 */
export function PageLayout({
  title,
  tags,
  line,
  band,
  children,
}: {
  title: string
  /** The state of the thing, as `StatusTag`s beside the title. */
  tags?: ReactNode
  /** The kind of the thing alone, under the title. Empty draws nothing. */
  line?: string | null
  /** Draws the heading alone in a band across the top of the page area. */
  band?: boolean
  children?: ReactNode
}) {
  const [slot, setSlot] = useState<HTMLElement | null>(null)
  const [named, setNamed] = useState<NamedPage | null>(null)
  const heading = (
    <PageHeading title={named?.name ?? title} tags={tags} line={named ? named.line : line} setActionsSlot={setSlot} />
  )

  return (
    <ActionsSlot.Provider value={slot}>
      <NameSlot.Provider value={setNamed}>
        <Flex vertical gap="middle">
          {band ? <HeaderBand>{heading}</HeaderBand> : heading}
          {children}
        </Flex>
      </NameSlot.Provider>
    </ActionsSlot.Provider>
  )
}

/**
 * The name of the page's heading and its line, given by a widget inside the page in place of the title, as a record's
 * page is headed by the record's name, see docs/reference/shell.md#page-header. It draws nothing. While it is mounted it
 * sets the heading, and it clears the heading when it unmounts, so the title shows again. Outside a `PageLayout`, as in a
 * widget preview, it does nothing.
 */
export function PageName({ name, line }: { name: string; line?: string | null }) {
  const setNamed = useContext(NameSlot)
  useLayoutEffect(() => {
    if (!setNamed) {
      return
    }
    setNamed({ name, line: line ?? null })
    return () => setNamed(null)
  }, [setNamed, name, line])
  return null
}

/**
 * The page's own actions, drawn on the right of its heading from wherever in the page they are written, so a widget
 * fills it as a page does, see docs/reference/shell.md#page-header. Outside a `PageLayout`, as in a widget preview, they
 * draw where they stand, in a row on the right. With `inPlace` they do so inside a `PageLayout` too, for a widget on a
 * designer's canvas, whose own page heading is not theirs.
 */
export function PageActions({ children, inPlace }: { children: ReactNode; inPlace?: boolean }) {
  const slot = useContext(ActionsSlot)
  if (inPlace || slot === undefined) {
    return (
      <Flex gap="small" justify="end" wrap>
        {children}
      </Flex>
    )
  }
  return slot ? createPortal(children, slot) : null
}

/**
 * The heading of a page that shows one thing, which only {@link PageLayout} draws, so a page never does: the title with its tags beside it, the
 * line under it and the actions' slot on the right.
 */
export function PageHeading({
  title,
  tags,
  line,
  setActionsSlot,
}: {
  title: string
  tags?: ReactNode
  line?: string | null
  setActionsSlot: (slot: HTMLElement | null) => void
}) {
  const { colour } = useDesignTokens()

  return (
    <Flex justify="space-between" align="center" gap="middle" data-testid="page-heading">
      <Flex vertical style={{ minWidth: 0 }}>
        <Flex gap="small" align="center">
          {/* Level one for the outline, at the 20px and 28px that a level-four heading has. */}
          <Typography.Title level={1} style={{ margin: 0, fontSize: 20, lineHeight: '28px' }}>
            {title}
          </Typography.Title>
          {tags}
        </Flex>
        {line ? <Typography.Text style={{ color: colour.textMuted }}>{line}</Typography.Text> : null}
      </Flex>
      <div data-testid="page-actions" ref={setActionsSlot} />
    </Flex>
  )
}

function HeaderBand({ children }: { children: ReactNode }) {
  const { colour, spacing } = useDesignTokens()
  const inset = useContext(ContentInsetContext)

  return (
    <div
      data-testid="page-band"
      style={{
        // Negative margins reach the edges of the content area, whose padding the shell gives through ContentInsetContext,
        // see docs/reference/shell.md#layout.
        margin: `-${inset}px -${inset}px 0`,
        padding: `${spacing.md}px ${inset}px`,
        background: colour.surface,
        borderBottom: `1px solid ${colour.borderSubtle}`,
      }}
    >
      {children}
    </div>
  )
}
