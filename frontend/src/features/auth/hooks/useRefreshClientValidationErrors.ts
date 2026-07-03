import { useEffect, useRef } from 'react';
import type { FieldPath, FieldValues, UseFormReturn } from 'react-hook-form';

export function useRefreshClientValidationErrors<TFieldValues extends FieldValues>(
  form: UseFormReturn<TFieldValues>,
  fields: readonly FieldPath<TFieldValues>[],
  language: string,
) {
  const previousLanguage = useRef(language);

  useEffect(() => {
    if (previousLanguage.current === language) return;
    previousLanguage.current = language;

    const fieldsWithClientErrors = fields.filter((field) => {
      const error = form.getFieldState(field, form.formState).error;
      return Boolean(error && error.type !== 'server');
    });

    if (fieldsWithClientErrors.length > 0) {
      void form.trigger(fieldsWithClientErrors);
    }
  }, [fields, form, language]);
}
