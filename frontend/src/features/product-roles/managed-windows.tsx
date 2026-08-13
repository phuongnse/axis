import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ShieldCheck, ShieldMinus } from 'lucide-react';
import { type FormEvent, useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  ManagedDialog,
  ManagedDialogAction,
  ManagedDialogAsyncAction,
  ManagedDialogBody,
} from '@/components/shared/ManagedDialog';
import {
  type ManagedWindowDescriptor,
  type ManagedWindowRendererProps,
  type ManagedWindowRendererRegistry,
  useCurrentManagedWindow,
} from '@/components/shared/ManagedWindowManager';
import { StatusBadge } from '@/components/shared/StatusBadge';
import { StatusNotice, type StatusNoticeTone } from '@/components/shared/StatusNotice';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog';
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ApiError } from '@/lib/api';
import type {
  AssignableSubjectDto,
  ProductRoleAssignmentDto,
  ProductRoleManagementResponse,
  ProductRoleOptionDto,
} from '@/lib/api-generated';
import { moduleNavigationAvailabilityKeys } from '@/lib/module-navigation-api';
import { assignProductRole, productRoleQueryKeys, revokeProductRole } from './api';

const PRODUCT_ROLE_ASSIGN_KIND = 'product-roles.assign';
const PRODUCT_ROLE_ASSIGNMENT_KIND = 'product-roles.assignment';
type Feedback = { tone: StatusNoticeTone; title: string; body: string };
type AssignmentPayload = {
  assignment: ProductRoleAssignmentDto;
  subject?: AssignableSubjectDto;
  role?: ProductRoleOptionDto;
};

export function productRoleAssignWindowDescriptor(
  management: ProductRoleManagementResponse,
  title: string,
): ManagedWindowDescriptor {
  return {
    id: 'product-roles:assign',
    kind: PRODUCT_ROLE_ASSIGN_KIND,
    resourceKey: 'assign',
    title,
    payload: management,
  };
}

export function productRoleAssignmentWindowDescriptor(
  assignment: ProductRoleAssignmentDto,
  subject: AssignableSubjectDto | undefined,
  role: ProductRoleOptionDto | undefined,
  title: string,
): ManagedWindowDescriptor {
  return {
    id: `product-roles:${assignment.subject?.kind}:${assignment.subject?.subjectId}:${assignment.policyVersionId}:${assignment.roleKey}`,
    kind: PRODUCT_ROLE_ASSIGNMENT_KIND,
    resourceKey: `${assignment.policyVersionId}:${assignment.roleKey}`,
    title,
    payload: { assignment, subject, role } satisfies AssignmentPayload,
  };
}

export const productRolesManagedWindowRenderers: ManagedWindowRendererRegistry = {
  [PRODUCT_ROLE_ASSIGN_KIND]: ProductRoleAssignWindowRenderer,
  [PRODUCT_ROLE_ASSIGNMENT_KIND]: ProductRoleAssignmentWindowRenderer,
};

function ProductRoleAssignWindowRenderer({ descriptor }: ManagedWindowRendererProps) {
  const { t } = useTranslation();
  const { windowId, closeWindow } = useCurrentManagedWindow();
  const management = readManagement(descriptor);
  if (!management) {
    return <UnavailableDialog title={descriptor.title} onClose={() => closeWindow(windowId)} />;
  }
  return (
    <ProductRoleAssignDialog
      management={management}
      onClose={() => closeWindow(windowId)}
      unavailableText={t('productRoles.assignmentUnavailable')}
    />
  );
}

function ProductRoleAssignmentWindowRenderer({ descriptor }: ManagedWindowRendererProps) {
  const { windowId, closeWindow } = useCurrentManagedWindow();
  const payload = readAssignment(descriptor);
  if (!payload) {
    return <UnavailableDialog title={descriptor.title} onClose={() => closeWindow(windowId)} />;
  }
  return (
    <ProductRoleAssignmentDialog initialPayload={payload} onClose={() => closeWindow(windowId)} />
  );
}

function ProductRoleAssignDialog({
  management,
  onClose,
  unavailableText,
}: {
  management: Required<Pick<ProductRoleManagementResponse, 'subjects' | 'roles' | 'assignments'>>;
  onClose: () => void;
  unavailableText: string;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const baseId = useId();
  const formId = `${baseId}-form`;
  const subjectId = `${baseId}-subject`;
  const roleId = `${baseId}-role`;
  const [subjectValue, setSubjectValue] = useState('');
  const [roleValue, setRoleValue] = useState('');
  const [assignments, setAssignments] = useState(management.assignments);
  const [idempotencyKey, setIdempotencyKey] = useState(createIdempotencyKey);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const [discardOpen, setDiscardOpen] = useState(false);
  const selectedSubject = management.subjects.find(
    (subject) => subjectKey(subject) === subjectValue,
  );
  const selectedRole = management.roles.find((role) => roleKey(role) === roleValue);
  const dirty = Boolean(subjectValue || roleValue);
  const mutation = useMutation({
    mutationFn: ({
      subject,
      role,
    }: {
      subject: AssignableSubjectDto;
      role: ProductRoleOptionDto;
    }) => {
      const existing = findAssignment(assignments, subject, role);
      return assignProductRole(
        {
          target: subject.subject,
          policyVersionId: role.policyVersionId,
          roleKey: role.roleKey,
          expectedRevision: existing?.revision ?? null,
        },
        idempotencyKey,
      );
    },
    onSuccess: async (result) => {
      setAssignments((current) => upsertAssignment(current, result));
      setSubjectValue('');
      setRoleValue('');
      setIdempotencyKey(createIdempotencyKey());
      setFeedback({
        tone: 'success',
        title: t('productRoles.assigned'),
        body: t('productRoles.assignedDescription'),
      });
      await invalidateProductRoleQueries(queryClient);
    },
    onError: (error) => setFeedback(roleProblemFeedback(error, t)),
  });

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setFeedback(null);
    if (selectedSubject && selectedRole)
      mutation.mutate({ subject: selectedSubject, role: selectedRole });
  }

  function requestClose() {
    if (dirty) setDiscardOpen(true);
    else onClose();
  }

  return (
    <>
      <ManagedDialog
        surfaceId="product-role-windows"
        open
        title={t('productRoles.assignTitle')}
        description={t('productRoles.assignDescription')}
        dirty={dirty}
        closeDisabled={mutation.isPending}
        onOpenChange={(open) => {
          if (!open) requestClose();
        }}
        footer={
          <>
            <ManagedDialogAction
              type="button"
              variant="outline"
              disabled={mutation.isPending}
              onClick={requestClose}
            >
              {t('app.cancel')}
            </ManagedDialogAction>
            <ManagedDialogAsyncAction
              type="submit"
              form={formId}
              disabled={mutation.isPending || !selectedSubject || !selectedRole}
              icon={<ShieldCheck aria-hidden />}
              pending={mutation.isPending}
              pendingLabel={t('productRoles.assigning')}
            >
              {t('productRoles.assign')}
            </ManagedDialogAsyncAction>
          </>
        }
      >
        <form id={formId} className="contents" onSubmit={submit}>
          <ManagedDialogBody className="space-y-4">
            {feedback ? (
              <div aria-live="polite">
                <StatusNotice tone={feedback.tone} title={feedback.title}>
                  {feedback.body}
                </StatusNotice>
              </div>
            ) : null}
            {management.subjects.length === 0 || management.roles.length === 0 ? (
              <StatusNotice tone="warning" title={unavailableText}>
                {management.subjects.length === 0
                  ? t('productRoles.noSubjects')
                  : t('productRoles.noRoles')}
              </StatusNotice>
            ) : (
              <>
                <Field>
                  <FieldLabel htmlFor={subjectId}>{t('productRoles.subject')}</FieldLabel>
                  <Select
                    value={subjectValue}
                    onValueChange={(value) => {
                      setSubjectValue(value ?? '');
                      setIdempotencyKey(createIdempotencyKey());
                    }}
                    disabled={mutation.isPending}
                  >
                    <SelectTrigger id={subjectId} className="w-full">
                      <SelectValue>
                        {selectedSubject?.displayName ?? t('productRoles.selectSubject')}
                      </SelectValue>
                    </SelectTrigger>
                    <SelectContent>
                      {management.subjects.map((subject) => (
                        <SelectItem key={subjectKey(subject)} value={subjectKey(subject)}>
                          <span>{subject.displayName ?? t('productRoles.unknownSubject')}</span>
                          {subject.secondaryLabel ? (
                            <span className="ml-2 text-muted-foreground">
                              {subject.secondaryLabel}
                            </span>
                          ) : null}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <FieldDescription>{t('productRoles.subjectHelp')}</FieldDescription>
                </Field>
                <Field>
                  <FieldLabel htmlFor={roleId}>{t('productRoles.role')}</FieldLabel>
                  <Select
                    value={roleValue}
                    onValueChange={(value) => {
                      setRoleValue(value ?? '');
                      setIdempotencyKey(createIdempotencyKey());
                    }}
                    disabled={mutation.isPending}
                  >
                    <SelectTrigger id={roleId} className="w-full">
                      <SelectValue>
                        {selectedRole?.displayName ?? t('productRoles.selectRole')}
                      </SelectValue>
                    </SelectTrigger>
                    <SelectContent>
                      {management.roles.map((role) => (
                        <SelectItem key={roleKey(role)} value={roleKey(role)}>
                          {role.displayName ?? role.roleKey}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <FieldDescription>
                    {selectedRole?.description ?? t('productRoles.roleHelp')}
                  </FieldDescription>
                </Field>
              </>
            )}
          </ManagedDialogBody>
        </form>
      </ManagedDialog>
      <AlertDialog open={discardOpen} onOpenChange={setDiscardOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('productRoles.discardTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('productRoles.discardDescription')}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('productRoles.keepEditing')}</AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              onClick={() => {
                setDiscardOpen(false);
                onClose();
              }}
            >
              {t('productRoles.discard')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}

function ProductRoleAssignmentDialog({
  initialPayload,
  onClose,
}: {
  initialPayload: AssignmentPayload;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [assignment, setAssignment] = useState(initialPayload.assignment);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const subjectName = initialPayload.subject?.displayName ?? t('productRoles.unknownSubject');
  const roleName =
    initialPayload.role?.displayName ?? assignment.roleKey ?? t('productRoles.notAvailable');
  const actionable =
    assignment.isActive === true &&
    assignment.subject !== undefined &&
    assignment.policyVersionId !== undefined &&
    assignment.roleKey !== undefined &&
    assignment.revision !== undefined;
  const mutation = useMutation({
    mutationFn: () =>
      revokeProductRole(
        {
          target: assignment.subject,
          policyVersionId: assignment.policyVersionId,
          roleKey: assignment.roleKey,
          expectedRevision: assignment.revision,
        },
        createIdempotencyKey(),
      ),
    onSuccess: async (result) => {
      setAssignment(result);
      setFeedback({
        tone: 'success',
        title: t('productRoles.revoked'),
        body: t('productRoles.revokedDescription'),
      });
      await invalidateProductRoleQueries(queryClient);
    },
    onError: (error) => setFeedback(roleProblemFeedback(error, t)),
  });

  return (
    <ManagedDialog
      surfaceId="product-role-windows"
      open
      title={subjectName}
      description={t('productRoles.currentDescription')}
      titleAccessory={
        <StatusBadge state={assignment.isActive ? 'positive' : 'inactive'}>
          {assignment.isActive ? t('productRoles.active') : t('productRoles.revokedStatus')}
        </StatusBadge>
      }
      closeDisabled={mutation.isPending}
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
      footer={
        <>
          <ManagedDialogAction
            type="button"
            variant="outline"
            disabled={mutation.isPending}
            onClick={onClose}
          >
            {t('app.close')}
          </ManagedDialogAction>
          {actionable ? (
            <AlertDialog>
              <AlertDialogTrigger
                render={
                  <ManagedDialogAsyncAction
                    type="button"
                    variant="destructive"
                    disabled={mutation.isPending}
                    icon={<ShieldMinus aria-hidden />}
                    pending={mutation.isPending}
                    pendingLabel={t('productRoles.revoking')}
                  >
                    {t('productRoles.revoke')}
                  </ManagedDialogAsyncAction>
                }
              />
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>{t('productRoles.revokeTitle')}</AlertDialogTitle>
                  <AlertDialogDescription>
                    {t('productRoles.revokeDescription', {
                      role: roleName,
                      subject: subjectName,
                    })}
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>{t('app.cancel')}</AlertDialogCancel>
                  <AlertDialogAction variant="destructive" onClick={() => mutation.mutate()}>
                    {t('productRoles.revoke')}
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          ) : null}
        </>
      }
    >
      <ManagedDialogBody className="space-y-4">
        {feedback ? (
          <div aria-live="polite">
            <StatusNotice tone={feedback.tone} title={feedback.title}>
              {feedback.body}
            </StatusNotice>
          </div>
        ) : null}
        <dl className="grid gap-4 text-sm sm:grid-cols-2">
          <Fact label={t('productRoles.subject')} value={subjectName} />
          <Fact label={t('productRoles.kind')} value={assignment.subject?.kind} />
          <Fact label={t('productRoles.role')} value={roleName} />
          <Fact
            label={t('productRoles.policy')}
            value={initialPayload.role?.policyKey ?? assignment.policyVersionId}
          />
        </dl>
      </ManagedDialogBody>
    </ManagedDialog>
  );
}

function UnavailableDialog({ title, onClose }: { title: string; onClose: () => void }) {
  const { t } = useTranslation();
  return (
    <ManagedDialog
      surfaceId="product-role-windows"
      open
      title={title}
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
      footer={
        <ManagedDialogAction type="button" variant="outline" onClick={onClose}>
          {t('app.close')}
        </ManagedDialogAction>
      }
    >
      <ManagedDialogBody>
        <p role="alert">{t('dialog.unavailable')}</p>
      </ManagedDialogBody>
    </ManagedDialog>
  );
}

function Fact({ label, value }: { label: string; value: string | undefined }) {
  const { t } = useTranslation();
  return (
    <div className="space-y-1">
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="break-all font-medium">{value ?? t('productRoles.notAvailable')}</dd>
    </div>
  );
}

function readManagement(
  descriptor: ManagedWindowDescriptor,
): Required<Pick<ProductRoleManagementResponse, 'subjects' | 'roles' | 'assignments'>> | null {
  const payload = descriptor.payload as ProductRoleManagementResponse | undefined;
  if (
    !payload ||
    !Array.isArray(payload.subjects) ||
    !Array.isArray(payload.roles) ||
    !Array.isArray(payload.assignments)
  ) {
    return null;
  }
  return { subjects: payload.subjects, roles: payload.roles, assignments: payload.assignments };
}

function readAssignment(descriptor: ManagedWindowDescriptor): AssignmentPayload | null {
  const payload = descriptor.payload as AssignmentPayload | undefined;
  return payload?.assignment ? payload : null;
}

function subjectKey(subject: AssignableSubjectDto): string {
  return `${subject.subject?.kind ?? ''}\u0000${subject.subject?.subjectId ?? ''}`;
}

function roleKey(role: ProductRoleOptionDto): string {
  return `${role.policyVersionId ?? ''}\u0000${role.roleKey ?? ''}`;
}

function findAssignment(
  assignments: ProductRoleAssignmentDto[],
  subject: AssignableSubjectDto,
  role: ProductRoleOptionDto,
) {
  return assignments.find(
    (assignment) =>
      assignment.subject?.kind === subject.subject?.kind &&
      assignment.subject?.subjectId === subject.subject?.subjectId &&
      assignment.policyVersionId === role.policyVersionId &&
      assignment.roleKey === role.roleKey,
  );
}

function upsertAssignment(
  assignments: ProductRoleAssignmentDto[],
  result: ProductRoleAssignmentDto,
): ProductRoleAssignmentDto[] {
  const index = assignments.findIndex(
    (assignment) =>
      assignment.subject?.kind === result.subject?.kind &&
      assignment.subject?.subjectId === result.subject?.subjectId &&
      assignment.policyVersionId === result.policyVersionId &&
      assignment.roleKey === result.roleKey,
  );
  if (index < 0) return [...assignments, result];
  return assignments.map((assignment, assignmentIndex) =>
    assignmentIndex === index ? result : assignment,
  );
}

function createIdempotencyKey(): string {
  return globalThis.crypto.randomUUID();
}

async function invalidateProductRoleQueries(
  queryClient: ReturnType<typeof useQueryClient>,
): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: productRoleQueryKeys.all }),
    queryClient.invalidateQueries({ queryKey: moduleNavigationAvailabilityKeys.all }),
  ]);
}

function roleProblemFeedback(error: unknown, t: (key: string) => string): Feedback {
  if (error instanceof ApiError && error.status === 409)
    return {
      tone: 'warning',
      title: t('productRoles.conflict'),
      body: t('productRoles.conflictDescription'),
    };
  if (error instanceof ApiError && (error.status === 403 || error.status === 404))
    return {
      tone: 'destructive',
      title: t('productRoles.unavailable'),
      body: t('productRoles.unavailableDescription'),
    };
  if (error instanceof ApiError && error.status === 503)
    return {
      tone: 'warning',
      title: t('productRoles.serviceUnavailable'),
      body: t('productRoles.serviceUnavailableDescription'),
    };
  return {
    tone: 'destructive',
    title: t('productRoles.actionFailed'),
    body: t('productRoles.actionFailedDescription'),
  };
}
