import type { TFunction } from 'i18next';
import { z } from 'zod';

export type RegisterFormValues = z.infer<ReturnType<typeof createRegisterSchema>>;

export function createRegisterSchema(t: TFunction) {
  return z
    .object({
      fullName: z.string().min(1, t('validation.fullNameRequired')),
      email: z.string().min(1, t('validation.emailRequired')).email(t('validation.emailInvalid')),
      password: z
        .string()
        .min(1, t('validation.passwordRequired'))
        .min(8, t('validation.passwordMin'))
        .regex(/[a-zA-Z]/, t('validation.passwordLetter'))
        .regex(/\d/, t('validation.passwordNumber')),
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
    });
}
