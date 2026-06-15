import type { TFunction } from 'i18next';
import { z } from 'zod';

export type RegisterOrganizationFormValues = z.infer<
  ReturnType<typeof createRegisterOrganizationSchema>
>;

export function createRegisterOrganizationSchema(t: TFunction) {
  return z.object({
    orgName: z
      .string()
      .trim()
      .min(1, t('validation.orgNameRequired'))
      .min(2, t('validation.orgNameMin'))
      .max(100, t('validation.orgNameMax')),
    organizationContactEmail: z
      .string()
      .trim()
      .min(1, t('validation.organizationEmailRequired'))
      .email(t('validation.emailInvalid')),
    acceptedTerms: z.boolean().refine((value) => value === true, {
      message: t('validation.acceptTerms'),
    }),
  });
}
