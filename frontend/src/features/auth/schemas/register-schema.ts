import { z } from 'zod';

import {
  isPasswordHardToGuess,
  PASSWORD_MAX_LENGTH,
  PASSWORD_MIN_LENGTH,
} from '@/features/auth/password-policy';

export type RegisterFormValues = z.infer<ReturnType<typeof createRegisterSchema>>;

export interface RegisterSchemaMessages {
  fullNameRequired: string;
  emailRequired: string;
  emailInvalid: string;
  passwordRequired: string;
  passwordMin: string;
  passwordMax: string;
  passwordConfirmationRequired: string;
  acceptTerms: string;
  passwordMismatch: string;
  passwordHarder: string;
}

const defaultRegisterSchemaMessages: RegisterSchemaMessages = {
  fullNameRequired: 'Full name is required',
  emailRequired: 'Email address is required',
  emailInvalid: 'Enter a valid email address',
  passwordRequired: 'Password is required',
  passwordMin: `Password must be at least ${PASSWORD_MIN_LENGTH} characters`,
  passwordMax: `Password must be ${PASSWORD_MAX_LENGTH} characters or fewer`,
  passwordConfirmationRequired: 'Password confirmation is required',
  acceptTerms: 'You must accept the Terms of Service and Privacy Policy',
  passwordMismatch: 'Passwords must match',
  passwordHarder: 'Choose a password that is harder to guess',
};

export function createRegisterSchema(
  messages: RegisterSchemaMessages = defaultRegisterSchemaMessages,
) {
  return z
    .object({
      fullName: z.string().min(1, messages.fullNameRequired),
      email: z.string().min(1, messages.emailRequired).email(messages.emailInvalid),
      password: z
        .string()
        .min(1, messages.passwordRequired)
        .min(PASSWORD_MIN_LENGTH, messages.passwordMin)
        .max(PASSWORD_MAX_LENGTH, messages.passwordMax),
      passwordConfirmation: z.string().min(1, messages.passwordConfirmationRequired),
      acceptedTerms: z.boolean().refine((value) => value === true, {
        message: messages.acceptTerms,
      }),
    })
    .superRefine((values, context) => {
      if (values.passwordConfirmation !== values.password) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: messages.passwordMismatch,
          path: ['passwordConfirmation'],
        });
      }

      if (
        values.password.length >= PASSWORD_MIN_LENGTH &&
        values.password.length <= PASSWORD_MAX_LENGTH &&
        !isPasswordHardToGuess(values.password, [values.email, values.fullName])
      ) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: messages.passwordHarder,
          path: ['password'],
        });
      }
    });
}
