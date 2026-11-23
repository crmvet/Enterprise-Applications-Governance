// The Academy palette for code that needs a color as a value (SVG fills and strokes in the dependency map and the
// timeline chart). Everything else uses the Tailwind tokens in styles/globals.css, which hold the same colors.
// Light only: there is no dark theme, on purpose (D-18).
export const palette = {
  navy: '#0a2a4a',
  navy2: '#1f3f5f',
  navy3: '#4a6582',
  navy4: '#6a8199',
  blue: '#0071dc',
  blueDark: '#005bb3',
  gold: '#ffc221',
  goldDark: '#8a6100',
  white: '#ffffff',
  lightBlue: '#e8f1fb',
  lightGold: '#fff4d6',
  lightGrey: '#f3f5f8',
  line: '#dfe5ec',
  lineStrong: '#c5d0dc',
  muted: '#9aabbd',
} as const;
