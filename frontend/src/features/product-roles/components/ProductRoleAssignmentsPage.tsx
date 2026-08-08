import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ShieldCheck, ShieldMinus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
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
import { Button } from '@/components/ui/button';
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
  ProductRoleOptionDto,
} from '@/lib/api-generated';
import {
  assignProductRole,
  productRoleManagementQueryOptions,
  productRoleQueryKeys,
  revokeProductRole,
} from '../api';

type Feedback = { tone: StatusNoticeTone; title: string; body: string };

export function ProductRoleAssignmentsPage() {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? i18n.language;
  const queryClient = useQueryClient();
  const managementQuery = useQuery(productRoleManagementQueryOptions(language));
  const data = managementQuery.data;
  const subjects = data?.subjects ?? [];
  const roles = data?.roles ?? [];
  const assignments = (data?.assignments ?? []).filter((assignment) => assignment.isActive);
  const [subjectValue, setSubjectValue] = useState('');
  const [roleValue, setRoleValue] = useState('');
  const [assignKey, setAssignKey] = useState(createIdempotencyKey);
  const [feedback, setFeedback] = useState<Feedback | null>(null);

  const assignMutation = useMutation({
    mutationFn: ({
      subject,
      role,
    }: {
      subject: AssignableSubjectDto;
      role: ProductRoleOptionDto;
    }) => {
      const existing = findAssignment(data?.assignments ?? [], subject, role);
      return assignProductRole(
        {
          target: subject.subject,
          policyVersionId: role.policyVersionId,
          roleKey: role.roleKey,
          expectedRevision: existing?.revision ?? null,
        },
        assignKey,
      );
    },
    onSuccess: async () => {
      setAssignKey(createIdempotencyKey());
      setFeedback({
        tone: 'success',
        title: t('productRoles.assigned'),
        body: t('productRoles.assignedDescription'),
      });
      await queryClient.invalidateQueries({ queryKey: productRoleQueryKeys.all });
    },
    onError: (error) => setFeedback(roleProblemFeedback(error, t)),
  });
  const revokeMutation = useMutation({
    mutationFn: (assignment: ProductRoleAssignmentDto) =>
      revokeProductRole(
        {
          target: assignment.subject,
          policyVersionId: assignment.policyVersionId,
          roleKey: assignment.roleKey,
          expectedRevision: assignment.revision,
        },
        createIdempotencyKey(),
      ),
    onSuccess: async () => {
      setFeedback({
        tone: 'success',
        title: t('productRoles.revoked'),
        body: t('productRoles.revokedDescription'),
      });
      await queryClient.invalidateQueries({ queryKey: productRoleQueryKeys.all });
    },
    onError: (error) => setFeedback(roleProblemFeedback(error, t)),
  });
  const selectedSubject = subjects.find((subject) => subjectKey(subject) === subjectValue);
  const selectedRole = roles.find((role) => roleKey(role) === roleValue);
  const pending = assignMutation.isPending || revokeMutation.isPending;

  return (
    <section className="flex h-full min-h-0 w-full min-w-0 flex-col gap-5 overflow-auto p-4 sm:p-6">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">{t('productRoles.title')}</h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
          {t('productRoles.description')}
        </p>
      </header>
      {feedback ? (
        <div aria-live="polite">
          <StatusNotice tone={feedback.tone} title={feedback.title}>
            {feedback.body}
          </StatusNotice>
        </div>
      ) : null}
      {managementQuery.isLoading ? (
        <p className="text-sm text-muted-foreground" aria-live="polite">
          {t('productRoles.loading')}
        </p>
      ) : managementQuery.isError ? (
        <StatusNotice tone="destructive" title={t('productRoles.loadFailed')}>
          <Button
            type="button"
            size="sm"
            variant="outline"
            onClick={() => void managementQuery.refetch()}
          >
            {t('app.retry')}
          </Button>
        </StatusNotice>
      ) : (
        <>
          <section
            className="grid gap-4 border-b border-border pb-5"
            aria-labelledby="assign-role-title"
          >
            <div>
              <h2 id="assign-role-title" className="text-lg font-medium">
                {t('productRoles.assignTitle')}
              </h2>
              <p className="text-sm text-muted-foreground">{t('productRoles.assignDescription')}</p>
            </div>
            {subjects.length === 0 || roles.length === 0 ? (
              <StatusNotice tone="warning" title={t('productRoles.assignmentUnavailable')}>
                {subjects.length === 0 ? t('productRoles.noSubjects') : t('productRoles.noRoles')}
              </StatusNotice>
            ) : (
              <div className="grid gap-4 lg:grid-cols-3 lg:items-end">
                <Field>
                  <FieldLabel htmlFor="role-subject">{t('productRoles.subject')}</FieldLabel>
                  <Select
                    value={subjectValue}
                    onValueChange={(value) => {
                      setSubjectValue(value ?? '');
                      setAssignKey(createIdempotencyKey());
                    }}
                    disabled={pending}
                  >
                    <SelectTrigger id="role-subject" className="w-full">
                      <SelectValue>
                        {selectedSubject?.displayName ?? t('productRoles.selectSubject')}
                      </SelectValue>
                    </SelectTrigger>
                    <SelectContent>
                      {subjects.map((subject) => (
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
                  <FieldLabel htmlFor="product-role">{t('productRoles.role')}</FieldLabel>
                  <Select
                    value={roleValue}
                    onValueChange={(value) => {
                      setRoleValue(value ?? '');
                      setAssignKey(createIdempotencyKey());
                    }}
                    disabled={pending}
                  >
                    <SelectTrigger id="product-role" className="w-full">
                      <SelectValue>
                        {selectedRole?.displayName ?? t('productRoles.selectRole')}
                      </SelectValue>
                    </SelectTrigger>
                    <SelectContent>
                      {roles.map((role) => (
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
                <Button
                  type="button"
                  disabled={pending || !selectedSubject || !selectedRole}
                  onClick={() =>
                    selectedSubject &&
                    selectedRole &&
                    assignMutation.mutate({ subject: selectedSubject, role: selectedRole })
                  }
                >
                  <ShieldCheck aria-hidden />
                  {assignMutation.isPending
                    ? t('productRoles.assigning')
                    : t('productRoles.assign')}
                </Button>
              </div>
            )}
          </section>
          <section className="grid gap-4" aria-labelledby="assignment-list-title">
            <div>
              <h2 id="assignment-list-title" className="text-lg font-medium">
                {t('productRoles.currentTitle')}
              </h2>
              <p className="text-sm text-muted-foreground">
                {t('productRoles.currentDescription')}
              </p>
            </div>
            {assignments.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t('productRoles.empty')}</p>
            ) : (
              <ul className="grid gap-3" aria-label={t('productRoles.listLabel')}>
                {assignments.map((assignment) => (
                  <AssignmentRow
                    key={assignmentKey(assignment)}
                    assignment={assignment}
                    subjects={subjects}
                    roles={roles}
                    disabled={pending}
                    onRevoke={() => revokeMutation.mutate(assignment)}
                  />
                ))}
              </ul>
            )}
          </section>
        </>
      )}
    </section>
  );
}

function AssignmentRow({
  assignment,
  subjects,
  roles,
  disabled,
  onRevoke,
}: {
  assignment: ProductRoleAssignmentDto;
  subjects: AssignableSubjectDto[];
  roles: ProductRoleOptionDto[];
  disabled: boolean;
  onRevoke: () => void;
}) {
  const { t } = useTranslation();
  const subject = subjects.find((candidate) => sameSubject(candidate.subject, assignment.subject));
  const role = roles.find(
    (candidate) =>
      candidate.policyVersionId === assignment.policyVersionId &&
      candidate.roleKey === assignment.roleKey,
  );
  return (
    <li className="grid gap-3 border-b border-border pb-3 last:border-0 sm:grid-cols-3 sm:items-center">
      <div className="min-w-0">
        <p className="truncate font-medium">
          {subject?.displayName ?? t('productRoles.unknownSubject')}
        </p>
        <p className="break-all text-xs text-muted-foreground">
          {assignment.subject?.kind} · {assignment.subject?.subjectId}
        </p>
      </div>
      <div className="min-w-0">
        <p className="truncate font-medium">{role?.displayName ?? assignment.roleKey}</p>
        {role?.description ? (
          <p className="text-sm text-muted-foreground">{role.description}</p>
        ) : null}
        <div className="mt-1 flex flex-wrap gap-2">
          <StatusBadge tone="success">{t('productRoles.active')}</StatusBadge>
          <span className="break-all text-xs text-muted-foreground">
            {assignment.policyVersionId}
          </span>
        </div>
      </div>
      <AlertDialog>
        <AlertDialogTrigger
          render={
            <Button type="button" size="sm" variant="destructive" disabled={disabled}>
              <ShieldMinus aria-hidden />
              {t('productRoles.revoke')}
            </Button>
          }
        />
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('productRoles.revokeTitle')}</AlertDialogTitle>
            <AlertDialogDescription>
              {t('productRoles.revokeDescription', {
                role: role?.displayName ?? assignment.roleKey ?? '',
                subject: subject?.displayName ?? t('productRoles.unknownSubject'),
              })}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('app.cancel')}</AlertDialogCancel>
            <AlertDialogAction variant="destructive" onClick={onRevoke}>
              {t('productRoles.revoke')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </li>
  );
}

function subjectKey(subject: AssignableSubjectDto): string {
  return `${subject.subject?.kind ?? ''}\u0000${subject.subject?.subjectId ?? ''}`;
}
function roleKey(role: ProductRoleOptionDto): string {
  return `${role.policyVersionId ?? ''}\u0000${role.roleKey ?? ''}`;
}
function assignmentKey(assignment: ProductRoleAssignmentDto): string {
  return `${assignment.subject?.kind}-${assignment.subject?.subjectId}-${assignment.policyVersionId}-${assignment.roleKey}`;
}
function sameSubject(
  left: AssignableSubjectDto['subject'],
  right: ProductRoleAssignmentDto['subject'],
): boolean {
  return left?.kind === right?.kind && left?.subjectId === right?.subjectId;
}
function findAssignment(
  assignments: ProductRoleAssignmentDto[],
  subject: AssignableSubjectDto,
  role: ProductRoleOptionDto,
) {
  return assignments.find(
    (assignment) =>
      sameSubject(subject.subject, assignment.subject) &&
      assignment.policyVersionId === role.policyVersionId &&
      assignment.roleKey === role.roleKey,
  );
}
function createIdempotencyKey(): string {
  return globalThis.crypto.randomUUID();
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
