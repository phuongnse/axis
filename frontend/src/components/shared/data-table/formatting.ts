import type { DataTableCellDefinition, DataTableMessages } from './types';

export interface DataTableValueFormatter {
  format: (value: unknown, definition: DataTableCellDefinition) => string;
}

const defaultDateFormat: Intl.DateTimeFormatOptions = { dateStyle: 'medium' };
const defaultDateTimeFormat: Intl.DateTimeFormatOptions = {
  dateStyle: 'medium',
  timeStyle: 'short',
};

export function createDataTableValueFormatter(
  locale: string,
  messages: Pick<DataTableMessages, 'trueValue' | 'falseValue' | 'emptyValue'>,
): DataTableValueFormatter {
  const numberFormatters = new Map<string, Intl.NumberFormat>();
  const dateFormatters = new Map<string, Intl.DateTimeFormat>();

  return {
    format(value, definition) {
      if (isEmpty(value)) return messages.emptyValue;

      if (definition.kind === 'number') {
        if (typeof value !== 'number' && typeof value !== 'bigint') return messages.emptyValue;
        if (typeof value === 'number' && !Number.isFinite(value)) return messages.emptyValue;
        const options = definition.format ?? {};
        const formatter = getNumberFormatter(numberFormatters, locale, options);
        return formatter.format(value);
      }

      if (definition.kind === 'version' || definition.kind === 'revision') {
        const formatted = primitiveValue(value);
        if (!formatted) return messages.emptyValue;
        const prefix = definition.kind === 'version' ? 'v' : 'r';
        return formatted.toLocaleLowerCase().startsWith(prefix)
          ? formatted
          : `${prefix}${formatted}`;
      }

      if (definition.kind === 'date' || definition.kind === 'dateTime') {
        const date = toDate(value, definition.kind === 'date');
        if (!date) return messages.emptyValue;
        const options =
          definition.format ??
          (definition.kind === 'date' ? defaultDateFormat : defaultDateTimeFormat);
        const formatter = getDateFormatter(dateFormatters, locale, options);
        return formatter.format(date);
      }

      if (definition.kind === 'boolean') {
        if (typeof value !== 'boolean') return messages.emptyValue;
        return value ? messages.trueValue : messages.falseValue;
      }

      if (definition.kind === 'list') {
        if (!Array.isArray(value)) return primitiveValue(value) ?? messages.emptyValue;
        const items = value.flatMap((item) => {
          const formatted = primitiveValue(item);
          return formatted ? [formatted] : [];
        });
        return items.length > 0 ? items.join(', ') : messages.emptyValue;
      }

      if (definition.kind === 'actor') {
        if (typeof value === 'object' && value !== null && 'displayName' in value) {
          return primitiveValue(value.displayName) ?? messages.emptyValue;
        }
        return messages.emptyValue;
      }

      return primitiveValue(value) ?? messages.emptyValue;
    },
  };
}

function getNumberFormatter(
  cache: Map<string, Intl.NumberFormat>,
  locale: string,
  options: Intl.NumberFormatOptions,
): Intl.NumberFormat {
  const key = JSON.stringify(options);
  const cached = cache.get(key);
  if (cached) return cached;
  const formatter = new Intl.NumberFormat(locale, options);
  cache.set(key, formatter);
  return formatter;
}

function getDateFormatter(
  cache: Map<string, Intl.DateTimeFormat>,
  locale: string,
  options: Intl.DateTimeFormatOptions,
): Intl.DateTimeFormat {
  const key = JSON.stringify(options);
  const cached = cache.get(key);
  if (cached) return cached;
  const formatter = new Intl.DateTimeFormat(locale, options);
  cache.set(key, formatter);
  return formatter;
}

function toDate(value: unknown, dateOnly: boolean): Date | null {
  if (value instanceof Date) return Number.isNaN(value.getTime()) ? null : value;

  if (dateOnly && typeof value === 'string') {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
    if (match) {
      const year = Number(match[1]);
      const month = Number(match[2]);
      const day = Number(match[3]);
      const date = new Date(year, month - 1, day);
      return date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day
        ? date
        : null;
    }
  }

  if (typeof value !== 'string' && typeof value !== 'number') return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

function primitiveValue(value: unknown): string | null {
  if (typeof value === 'string') return value.trim() || null;
  if (typeof value === 'number') return Number.isFinite(value) ? String(value) : null;
  if (typeof value === 'bigint') return String(value);
  return null;
}

function isEmpty(value: unknown): boolean {
  return value === null || value === undefined || value === '';
}
