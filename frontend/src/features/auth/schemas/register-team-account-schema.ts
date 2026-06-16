import type { TFunction } from 'i18next';
import { z } from 'zod';

export type RegisterTeamAccountFormValues = z.infer<
  ReturnType<typeof createRegisterTeamAccountSchema>
>;

export function createRegisterTeamAccountSchema(t: TFunction) {
  return z.object({
    teamAccountName: z
      .string()
      .trim()
      .min(1, t('validation.teamAccountNameRequired'))
      .min(2, t('validation.teamAccountNameMin'))
      .max(100, t('validation.teamAccountNameMax')),
    teamContactEmail: z
      .string()
      .trim()
      .min(1, t('validation.teamAccountEmailRequired'))
      .email(t('validation.emailInvalid')),
    acceptedTerms: z.boolean().refine((value) => value === true, {
      message: t('validation.acceptTerms'),
    }),
  });
}
