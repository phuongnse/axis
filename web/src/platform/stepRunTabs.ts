/** Proposed, see the review on #3650: the tabs of a step run's side panel, in order, each kept in the address. */
export const stepRunTabs = ['overview', 'input', 'output', 'attempts', 'error'] as const
export type StepRunTab = (typeof stepRunTabs)[number]
