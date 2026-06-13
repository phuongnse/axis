import type { TFunction } from 'i18next';
import { z } from 'zod';

export type LoginFormValues = z.infer<ReturnType<typeof createLoginSchema>>;

export function createLoginSchema(t: TFunction) {
  return z.object({
    email: z.string().min(1, t('validation.emailRequired')).email(t('validation.emailInvalid')),
    password: z.string().min(1, t('validation.passwordRequired')),
  });
}
