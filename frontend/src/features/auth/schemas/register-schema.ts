import { z } from 'zod';

import {
  isPasswordHardToGuess,
  PASSWORD_MAX_LENGTH,
  PASSWORD_MIN_LENGTH,
} from '@/features/auth/password-policy';

export type RegisterFormValues = z.infer<ReturnType<typeof createRegisterSchema>>;

export function createRegisterSchema() {
  return z
    .object({
      fullName: z.string().min(1, 'Full name is required'),
      email: z.string().min(1, 'Email address is required').email('Enter a valid email address'),
      password: z
        .string()
        .min(1, 'Password is required')
        .min(PASSWORD_MIN_LENGTH, `Password must be at least ${PASSWORD_MIN_LENGTH} characters`)
        .max(PASSWORD_MAX_LENGTH, `Password must be ${PASSWORD_MAX_LENGTH} characters or fewer`),
      passwordConfirmation: z.string().min(1, 'Password confirmation is required'),
      acceptedTerms: z.boolean().refine((value) => value === true, {
        message: 'You must accept the Terms of Service and Privacy Policy',
      }),
    })
    .superRefine((values, context) => {
      if (values.passwordConfirmation !== values.password) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'Passwords must match',
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
          message: 'Enter both first and last name',
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
          message: 'Choose a password that is harder to guess',
          path: ['password'],
        });
      }
    });
}
