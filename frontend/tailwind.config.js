import { createRequire } from 'node:module';
import {
  axisTailwindBackgroundImageTokens,
  axisTailwindColorTokens,
  axisTailwindRadiusTokens,
  axisTailwindShadowTokens,
} from './src/design-system/tailwind-tokens.js';

const require = createRequire(import.meta.url);

/** @type {import('tailwindcss').Config} */
export default {
  darkMode: ['class'],
  content: ['./index.html', './src/**/*.{ts,tsx,js,jsx}'],
  theme: {
    extend: {
      colors: axisTailwindColorTokens,
      borderRadius: axisTailwindRadiusTokens,
      boxShadow: axisTailwindShadowTokens,
      backgroundImage: axisTailwindBackgroundImageTokens,
    },
  },
  plugins: [require('tailwindcss-animate')],
};
