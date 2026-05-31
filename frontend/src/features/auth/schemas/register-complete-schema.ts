import { z } from 'zod';

export const registerCompleteSchema = z.object({
  organizationName: z
    .string()
    .trim()
    .min(2, 'Organization name must be at least 2 characters.')
    .max(100, 'Organization name must be at most 100 characters.'),
  fullName: z.string().trim().min(1, 'Full name is required.'),
  acceptedTerms: z.boolean().refine((value) => value, {
    message: 'You must accept the Terms of Service and Privacy Policy.',
  }),
});

export type RegisterCompleteFormValues = z.infer<typeof registerCompleteSchema>;
