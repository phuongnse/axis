import { Typography } from 'antd'
import type { StudioDiagnostic } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import type { StudioProblemItem } from './StudioProblems'

/** Proposed: a problem of the compiler, led by its severity in its colour and its code, muted. */
export function useDiagnosticItems(): (diagnostics: readonly StudioDiagnostic[]) => StudioProblemItem[] {
  const t = useText()
  const { colour } = useDesignTokens()
  return (diagnostics) =>
    [
      ...diagnostics.filter((diagnostic) => diagnostic.severity === 'error'),
      ...diagnostics.filter((diagnostic) => diagnostic.severity !== 'error'),
    ].map((diagnostic) => ({
      lead: (
        <>
          <Typography.Text style={{ color: diagnostic.severity === 'error' ? colour.dangerText : colour.warningText }}>
            {t(
              diagnostic.severity === 'error' ? 'shell.studio.problems.errorLead' : 'shell.studio.problems.warningLead',
            )}
          </Typography.Text>{' '}
          <Typography.Text style={{ color: colour.textMuted }}>{diagnostic.code}</Typography.Text>
        </>
      ),
      message: diagnostic.message,
      resourceId: diagnostic.resourceId,
      file: diagnostic.file,
      path: diagnostic.path,
    }))
}
