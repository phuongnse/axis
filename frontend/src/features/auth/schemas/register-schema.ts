import type { TFunction } from 'i18next';
import { z } from 'zod';

import {
  isPasswordHardToGuess,
  PASSWORD_MAX_LENGTH,
  PASSWORD_MIN_LENGTH,
} from '@/features/auth/password-policy';

export type RegisterFormValues = z.infer<ReturnType<typeof createRegisterSchema>>;

export function createRegisterSchema(t: TFunction) {
  return z
    .object({
      fullName: z.string().min(1, t('validation.fullNameRequired')),
      email: z.string().min(1, t('validation.emailRequired')).email(t('validation.emailInvalid')),
      password: z
        .string()
        .min(1, t('validation.passwordRequired'))
        .min(PASSWORD_MIN_LENGTH, t('validation.passwordMin'))
        .max(PASSWORD_MAX_LENGTH, t('validation.passwordMax')),
      passwordConfirmation: z.string().min(1, t('validation.passwordConfirmationRequired')),
      acceptedTerms: z.boolean().refine((value) => value === true, {
        message: t('validation.acceptTerms'),
      }),
    })
    .superRefine((values, context) => {
      if (values.passwordConfirmation !== values.password) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: t('validation.passwordConfirmationMatch'),
          path: ['passwordConfirmation'],
        });
      }

      const nameParts = values.fullName
        .trim()
        .split(/\s+/)
        .filter((part) => part.length > 0);
      if (nameParts.length < 2) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: t('validation.firstLastName'),
          path: ['fullName'],
        });
      }

      if (
        values.password.length >= PASSWORD_MIN_LENGTH &&
        values.password.length <= PASSWORD_MAX_LENGTH &&
        !isPasswordHardToGuess(values.password, [values.email, values.fullName])
      ) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: t('validation.passwordCommon'),
          path: ['password'],
        });
      }
    });
}
