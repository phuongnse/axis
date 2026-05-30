import { z } from 'zod';

export const registerSchema = z
  .object({
    organizationName: z
      .string()
      .min(1, 'Organization name is required')
      .min(2, 'Organization name must be between 2 and 100 characters')
      .max(100, 'Organization name must be between 2 and 100 characters'),
    fullName: z.string().min(1, 'Full name is required'),
    email: z.string().min(1, 'Email address is required').email('Enter a valid email address'),
    password: z
      .string()
      .min(1, 'Password is required')
      .min(8, 'Password must be at least 8 characters')
      .regex(/[a-zA-Z]/, 'Password must contain at least one letter')
      .regex(/\d/, 'Password must contain at least one number'),
    passwordConfirmation: z.string().min(1, 'Password confirmation is required'),
    acceptedTerms: z.boolean().refine((value) => value === true, {
      message: 'You must accept the Terms of Service and Privacy Policy',
    }),
  })
  .superRefine((values, context) => {
    if (values.passwordConfirmation !== values.password) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Password confirmation must match password',
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
        message: 'Enter first and last name',
        path: ['fullName'],
      });
    }
  });

export type RegisterFormValues = z.infer<typeof registerSchema>;
