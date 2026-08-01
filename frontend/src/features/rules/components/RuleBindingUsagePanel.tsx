import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { MetadataTag } from '@/components/shared/MetadataTag';
import { StatusBadge } from '@/components/shared/StatusBadge';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { Button } from '@/components/ui/button';
import { deleteRuleBinding, ruleBindingUsageQueryOptions } from '../api';

export function RuleBindingUsagePanel({
  definitionKey,
  version,
  active,
}: {
  definitionKey: string;
  version: number;
  active: boolean;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [bindingToDelete, setBindingToDelete] = useState<string | null>(null);
  const usageQuery = useQuery({
    ...ruleBindingUsageQueryOptions(definitionKey, version),
    enabled: active && Boolean(definitionKey && version),
  });
  const usages = usageQuery.data ?? [];
  const deleteMutation = useMutation({
    mutationFn: deleteRuleBinding,
    onSuccess: async () => {
      setBindingToDelete(null);
      await queryClient.invalidateQueries({
        queryKey: ruleBindingUsageQueryOptions(definitionKey, version).queryKey,
      });
    },
  });

  return (
    <div data-slot="rule-binding-usage" className="space-y-4">
      <p className="text-sm leading-relaxed text-muted-foreground">
        {t('rules.bindingUsageDescription')}
      </p>
      {usageQuery.isLoading ? <p role="status">{t('rules.bindingUsageLoading')}</p> : null}
      {usageQuery.isError ? (
        <p role="alert" className="text-sm text-destructive">
          {t('rules.bindingUsageError')}
        </p>
      ) : null}
      {!usageQuery.isLoading && !usageQuery.isError && usages.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('rules.bindingUsageEmpty')}</p>
      ) : null}
      {usages.length > 0 ? (
        <ul className="divide-y divide-border">
          {usages.map((usage) => (
            <li key={usage.bindingId} className="space-y-3 py-4 first:pt-0 last:pb-0">
              <div className="flex flex-wrap items-center gap-2">
                <MetadataTag>{usage.targetType ?? '—'}</MetadataTag>
                <StatusBadge tone={usage.enabled ? 'success' : 'muted'}>
                  {usage.enabled ? t('rules.bindingEnabled') : t('rules.bindingDisabled')}
                </StatusBadge>
              </div>
              <dl className="grid gap-3 text-sm sm:grid-cols-2">
                <UsageFact label={t('rules.bindingTarget')} value={usage.targetId ?? '—'} />
                <UsageFact
                  label={t('rules.bindingTrigger')}
                  value={usage.useCaseOrTrigger ?? '—'}
                />
                <UsageFact label={t('rules.bindingPriority')} value={String(usage.priority ?? 0)} />
                <UsageFact label={t('rules.bindingId')} value={usage.bindingId ?? '—'} />
              </dl>
              <Button
                type="button"
                variant="destructive"
                size="sm"
                onClick={() => setBindingToDelete(usage.bindingId ?? null)}
                disabled={!usage.bindingId || deleteMutation.isPending}
              >
                <Trash2 aria-hidden />
                {t('rules.bindingRemove')}
              </Button>
            </li>
          ))}
        </ul>
      ) : null}
      <AlertDialog
        open={bindingToDelete !== null}
        onOpenChange={(nextOpen) => {
          if (!nextOpen && !deleteMutation.isPending) setBindingToDelete(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('rules.bindingRemoveTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('rules.bindingRemoveDescription')}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deleteMutation.isPending}>
              {t('app.cancel')}
            </AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              disabled={deleteMutation.isPending}
              onClick={() => {
                if (bindingToDelete) deleteMutation.mutate(bindingToDelete);
              }}
            >
              {t('rules.bindingRemove')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

function UsageFact({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="break-words font-medium text-foreground">{value}</dd>
    </div>
  );
}
