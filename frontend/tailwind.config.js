import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);

/** @type {import('tailwindcss').Config} */
export default {
  darkMode: ['class'],
  content: ['./index.html', './src/**/*.{ts,tsx,js,jsx}'],
  theme: {
    extend: {
      colors: {
        background: 'hsl(var(--background))',
        foreground: 'hsl(var(--foreground))',
        border: 'hsl(var(--border))',
        input: 'hsl(var(--input))',
        ring: 'hsl(var(--ring))',
        primary: {
          DEFAULT: 'hsl(var(--primary))',
          foreground: 'hsl(var(--primary-foreground))',
        },
        secondary: {
          DEFAULT: 'hsl(var(--secondary))',
          foreground: 'hsl(var(--secondary-foreground))',
        },
        destructive: {
          DEFAULT: 'hsl(var(--destructive))',
          foreground: 'hsl(var(--destructive-foreground))',
        },
        muted: {
          DEFAULT: 'hsl(var(--muted))',
          foreground: 'hsl(var(--muted-foreground))',
        },
        accent: {
          DEFAULT: 'hsl(var(--accent))',
          foreground: 'hsl(var(--accent-foreground))',
        },
        popover: {
          DEFAULT: 'hsl(var(--popover))',
          foreground: 'hsl(var(--popover-foreground))',
        },
        card: {
          DEFAULT: 'hsl(var(--card))',
          foreground: 'hsl(var(--card-foreground))',
        },
        chart: {
          1: 'hsl(var(--chart-1))',
          2: 'hsl(var(--chart-2))',
          3: 'hsl(var(--chart-3))',
          4: 'hsl(var(--chart-4))',
          5: 'hsl(var(--chart-5))',
        },
        state: {
          info: {
            DEFAULT: 'hsl(var(--state-info))',
            foreground: 'hsl(var(--state-info-foreground))',
            background: 'hsl(var(--state-info-background))',
            border: 'hsl(var(--state-info-border))',
          },
          success: {
            DEFAULT: 'hsl(var(--state-success))',
            foreground: 'hsl(var(--state-success-foreground))',
            background: 'hsl(var(--state-success-background))',
            border: 'hsl(var(--state-success-border))',
          },
          warning: {
            DEFAULT: 'hsl(var(--state-warning))',
            foreground: 'hsl(var(--state-warning-foreground))',
            background: 'hsl(var(--state-warning-background))',
            border: 'hsl(var(--state-warning-border))',
          },
        },
        inverse: {
          DEFAULT: 'hsl(var(--inverse))',
          foreground: 'hsl(var(--inverse-foreground))',
          muted: 'hsl(var(--inverse-muted))',
          border: 'hsl(var(--inverse-border))',
        },
        sidebar: {
          DEFAULT: 'hsl(var(--sidebar))',
          foreground: 'hsl(var(--sidebar-foreground))',
          muted: 'hsl(var(--sidebar-muted))',
          border: 'hsl(var(--sidebar-border))',
          accent: 'hsl(var(--sidebar-accent))',
          'accent-foreground': 'hsl(var(--sidebar-accent-foreground))',
        },
      },
      borderRadius: {
        lg: 'var(--radius)',
        md: 'calc(var(--radius) - 2px)',
        sm: 'calc(var(--radius) - 4px)',
      },
    },
  },
  plugins: [require('tailwindcss-animate')],
};
