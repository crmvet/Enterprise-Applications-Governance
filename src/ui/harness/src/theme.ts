// The theme colors (Fluent 2, D-35) for code that needs a color as a value (SVG fills and strokes in the dependency map and
// the timeline chart). Everything else uses the Tailwind tokens in styles/globals.css, which hold the same colors.
// Light only: there is no dark theme, on purpose (D-18).
export const palette = {
  navy: '#0a2a4a',
  navy2: '#1f3f5f',
  navy3: '#4a6582',
  navy4: '#6a8199',
  blue: '#0f6cbd',
  blueDark: '#115ea3',
  gold: '#f7630c',
  goldDark: '#bc4b09',
  white: '#ffffff',
  lightBlue: '#ebf3fc',
  lightGold: '#fff9f5',
  lightGrey: '#f5f5f5',
  line: '#e0e0e0',
  lineStrong: '#d1d1d1',
  muted: '#adadad',
} as const;
