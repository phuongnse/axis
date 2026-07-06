import { render, screen } from '@testing-library/react';
import type { ComponentProps, SVGProps } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { AuthNotice } from '../src/features/auth/components/AuthNotice';

vi.mock('lucide-react', async () => {
  function mockIcon(name: string) {
    return function MockNoticeIcon(props: SVGProps<SVGSVGElement>) {
      return <svg data-icon={name} data-testid="notice-icon" {...props} />;
    };
  }

  return {
    CircleAlert: mockIcon('error'),
    CircleCheck: mockIcon('success'),
    Info: mockIcon('info'),
    TriangleAlert: mockIcon('warning'),
  };
});

type AuthNoticeVariant = NonNullable<ComponentProps<typeof AuthNotice>['variant']>;

const variantIcons: Array<[AuthNoticeVariant, string]> = [
  ['default', 'info'],
  ['destructive', 'error'],
  ['success', 'success'],
  ['warning', 'warning'],
];

describe('AuthNotice', () => {
  it.each(variantIcons)('maps %s notices to the %s icon contract', (variant, iconName) => {
    render(
      <AuthNotice variant={variant} title="Notice title">
        Notice body
      </AuthNotice>,
    );

    const icon = screen.getByTestId('notice-icon');
    expect(icon).toHaveAttribute('data-icon', iconName);
    expect(icon).toHaveAttribute('aria-hidden', 'true');
  });
});
