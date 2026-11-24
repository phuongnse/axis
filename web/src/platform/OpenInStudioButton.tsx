import { useLinkClickHandler } from 'react-router'
import { AxisButton } from './AxisButton'
import { useText } from './texts'

interface OpenInStudioButtonProps {
  /** The Studio address to open, from `studioResourceAddress`, `studioApplicationAddress` or `studioReleaseAddress`. */
  address: string
  'data-testid'?: string
}

/**
 * The named button that opens a thing in Studio, a secondary button with a real address, so Ctrl+click opens a new tab.
 * Studio is another site, so it is never a link in the data, see docs/reference/actions-and-controls.md#opening-things.
 * Every caller shows it only for a person who may use Studio.
 */
export function OpenInStudioButton({ address, 'data-testid': testId }: OpenInStudioButtonProps) {
  const t = useText()
  const go = useLinkClickHandler(address)
  return (
    <AxisButton role="secondary" data-testid={testId} href={address} onClick={(event) => go(event as never)}>
      {t('shell.studio.openInStudio')}
    </AxisButton>
  )
}
