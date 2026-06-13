import { ZxcvbnFactory } from '@zxcvbn-ts/core';
import * as zxcvbnCommonPackage from '@zxcvbn-ts/language-common/dist/index.mjs';
import * as zxcvbnEnPackage from '@zxcvbn-ts/language-en/dist/index.mjs';

const PASSWORD_MIN_LENGTH = 15;
const PASSWORD_MAX_LENGTH = 128;
const PASSWORD_MIN_SCORE = 4;
const MINIMUM_SEQUENTIAL_RUN_LENGTH = 6;

const COMMON_PASSWORDS = new Set([
  '123456789012345',
  'adminadminadmin',
  'correcthorsebatterystaple',
  'letmeinletmein',
  'passwordpassword',
  'passwordpassword1',
  'qwertyqwerty123',
  'testpassword123',
  'welcome123456789',
]);

const KEYBOARD_SEQUENCES = ['qwertyuiop', 'asdfghjkl', 'zxcvbnm'];

const zxcvbn = new ZxcvbnFactory({
  dictionary: {
    ...zxcvbnCommonPackage.dictionary,
    ...zxcvbnEnPackage.dictionary,
  },
  graphs: zxcvbnCommonPackage.adjacencyGraphs,
  translations: zxcvbnEnPackage.translations,
});

function normalizePassword(value: string) {
  return value.trim().replace(/\s+/g, '').toLowerCase();
}

function getContextCandidates(values: string[]) {
  return values
    .flatMap((value) => {
      const normalized = normalizePassword(value);
      const atIndex = value.indexOf('@');
      const localPart = atIndex > 0 ? normalizePassword(value.slice(0, atIndex)) : '';
      return [normalized, localPart];
    })
    .filter((value) => value.length >= PASSWORD_MIN_LENGTH);
}

function hasRepeatedShortPattern(normalized: string) {
  const maxPatternLength = Math.min(4, Math.floor(normalized.length / 2));
  for (let patternLength = 1; patternLength <= maxPatternLength; patternLength += 1) {
    if (normalized.length % patternLength !== 0) {
      continue;
    }

    const pattern = normalized.slice(0, patternLength);
    if (pattern.repeat(normalized.length / patternLength) === normalized) {
      return true;
    }
  }

  return false;
}

function isNextCharacter(previous: string, current: string, ascending: boolean) {
  if (/^\d$/.test(previous) && /^\d$/.test(current)) {
    const previousValue = Number(previous);
    const currentValue = Number(current);
    const expected = ascending ? (previousValue + 1) % 10 : (previousValue + 9) % 10;
    return currentValue === expected;
  }

  if (/^[a-z]$/i.test(previous) && /^[a-z]$/i.test(current)) {
    const previousValue = previous.toLowerCase().charCodeAt(0) - 97;
    const currentValue = current.toLowerCase().charCodeAt(0) - 97;
    const expected = ascending ? (previousValue + 1) % 26 : (previousValue + 25) % 26;
    return currentValue === expected;
  }

  return false;
}

function hasSequentialRun(normalized: string, minimumRunLength: number) {
  let ascendingRun = 1;
  let descendingRun = 1;

  for (let index = 1; index < normalized.length; index += 1) {
    const previous = normalized[index - 1];
    const current = normalized[index];
    ascendingRun = isNextCharacter(previous, current, true) ? ascendingRun + 1 : 1;
    descendingRun = isNextCharacter(previous, current, false) ? descendingRun + 1 : 1;

    if (ascendingRun >= minimumRunLength || descendingRun >= minimumRunLength) {
      return true;
    }
  }

  return false;
}

function hasKeyboardSequence(normalized: string, minimumRunLength: number) {
  return KEYBOARD_SEQUENCES.some((sequence) => {
    const reversed = sequence.split('').reverse().join('');
    return [sequence, reversed].some((candidate) => {
      for (let index = 0; index <= candidate.length - minimumRunLength; index += 1) {
        if (normalized.includes(candidate.slice(index, index + minimumRunLength))) {
          return true;
        }
      }

      return false;
    });
  });
}

function isPasswordPredictable(password: string, contextValues: string[] = []) {
  const normalized = normalizePassword(password);
  if (COMMON_PASSWORDS.has(normalized)) {
    return true;
  }

  if (
    hasRepeatedShortPattern(normalized) ||
    hasSequentialRun(normalized, MINIMUM_SEQUENTIAL_RUN_LENGTH) ||
    hasKeyboardSequence(normalized, MINIMUM_SEQUENTIAL_RUN_LENGTH)
  ) {
    return true;
  }

  return getContextCandidates(contextValues).some((candidate) => candidate === normalized);
}

function getPasswordStrength(password: string, contextValues: string[] = []) {
  return zxcvbn.check(password, contextValues);
}

function isPasswordHardToGuess(password: string, contextValues: string[] = []) {
  if (password.length < PASSWORD_MIN_LENGTH || password.length > PASSWORD_MAX_LENGTH) {
    return false;
  }

  if (isPasswordPredictable(password, contextValues)) {
    return false;
  }

  return getPasswordStrength(password, contextValues).score >= PASSWORD_MIN_SCORE;
}

export {
  getPasswordStrength,
  isPasswordHardToGuess,
  isPasswordPredictable,
  PASSWORD_MAX_LENGTH,
  PASSWORD_MIN_LENGTH,
};
