import { render, screen } from '@testing-library/react';
import type { ComponentProps, SVGProps } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { StatusNotice } from '../src/components/shared/StatusNotice';

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

type StatusNoticeTone = NonNullable<ComponentProps<typeof StatusNotice>['tone']>;

const toneContracts: Array<[StatusNoticeTone, string, string]> = [
  ['info', 'info', 'bg-info/10'],
  ['success', 'success', 'bg-success/10'],
  ['warning', 'warning', 'bg-warning/10'],
  ['destructive', 'error', 'bg-destructive/10'],
];

describe('StatusNotice', () => {
  it.each(
    toneContracts,
  )('maps %s notices to the %s icon and semantic tone', (tone, iconName, backgroundClass) => {
    render(
      <StatusNotice tone={tone} title="Notice title">
        Notice body
      </StatusNotice>,
    );

    const notice = screen.getByRole('alert');
    const icon = screen.getByTestId('notice-icon');
    expect(notice).toHaveClass(backgroundClass);
    expect(icon).toHaveAttribute('data-icon', iconName);
    expect(icon).toHaveAttribute('aria-hidden', 'true');
    expect(screen.getByText('Notice title')).toBeInTheDocument();
    expect(screen.getByText('Notice body')).toBeInTheDocument();
  });
});
