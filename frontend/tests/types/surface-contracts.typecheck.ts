import type {
  EnforcedSurfaceContractId,
  SurfaceContractId,
  SurfaceIdFor,
} from '@/lib/ui-foundation';

type Equal<Left, Right> =
  (<Value>() => Value extends Left ? 1 : 2) extends <Value>() => Value extends Right ? 1 : 2
    ? true
    : false;
type Expect<Value extends true> = Value;

export type EnforcedContractLifecycleMatchesManifest = Expect<
  Equal<
    EnforcedSurfaceContractId,
    'account-surface' | 'authenticated-frame' | 'entry-surface' | 'managed-task-window'
  >
>;
export type SurfaceContractsMatchManifest = Expect<
  Equal<
    SurfaceContractId,
    | 'account-surface'
    | 'authenticated-frame'
    | 'entry-surface'
    | 'managed-task-window'
    | 'resource-workspace'
  >
>;
export type EntryConsumersRemainFinite = Expect<
  Equal<
    SurfaceIdFor<'entry-surface'>,
    | 'email-confirmation'
    | 'invitation-acceptance'
    | 'registration'
    | 'session-unavailable'
    | 'sign-in'
    | 'verify-email'
  >
>;
