import { z } from 'zod';

export interface SignInSchemaMessages {
  emailRequired: string;
  emailInvalid: string;
  passwordRequired: string;
}

const defaultSignInSchemaMessages: SignInSchemaMessages = {
  emailRequired: 'Email address is required',
  emailInvalid: 'Enter a valid email address',
  passwordRequired: 'Password is required',
};

export function createSignInSchema(messages: SignInSchemaMessages = defaultSignInSchemaMessages) {
  return z.object({
    email: z.string().min(1, messages.emailRequired).email(messages.emailInvalid),
    password: z.string().min(1, messages.passwordRequired),
  });
}

export const signInSchema = createSignInSchema();

export type SignInFormValues = z.infer<ReturnType<typeof createSignInSchema>>;
