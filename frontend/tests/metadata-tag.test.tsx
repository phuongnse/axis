import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { MetadataTag } from '@/components/shared/MetadataTag';

describe('MetadataTag', () => {
  it('renders compact peer metadata with one stable treatment', () => {
    render(<MetadataTag>Text</MetadataTag>);

    expect(screen.getByText('Text')).toHaveAttribute('data-slot', 'badge');
    expect(screen.getByText('Text')).toHaveAttribute('data-variant', 'secondary');
  });
});
