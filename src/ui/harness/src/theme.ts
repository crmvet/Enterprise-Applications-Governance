// The Academy palette as a Fluent UI v9 light theme. Light only: there is no dark theme, on purpose (D-18).
// Navy 0A2A4A for text and lines, blue 0071DC for brand and header bars, gold FFC221 as the accent.
// Fills: white FFFFFF, light blue E8F1FB, light gold FFF4D6, light gray F3F5F8.
import { createLightTheme, type BrandVariants, type Theme } from '@fluentui/react-components';

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

// A 16-step ramp around the Academy blue. Fluent uses step 80 for brand fills and brand text.
const academyBrand: BrandVariants = {
  10: '#020f1f',
  20: '#04213f',
  30: '#05305c',
  40: '#063d75',
  50: '#064a8e',
  60: '#0558aa',
  70: '#0365c4',
  80: '#0071dc',
  90: '#2a84e2',
  100: '#4c96e7',
  110: '#6aa8ec',
  120: '#87baf0',
  130: '#a3cbf4',
  140: '#bfdcf7',
  150: '#daebfb',
  160: '#eef5fd',
};

const base = createLightTheme(academyBrand);

export const academyTheme: Theme = {
  ...base,
  fontFamilyBase: "'Segoe UI', 'Segoe UI Web (West European)', -apple-system, BlinkMacSystemFont, Roboto, 'Helvetica Neue', sans-serif",
  colorNeutralForeground1: palette.navy,
  colorNeutralForeground1Hover: palette.navy,
  colorNeutralForeground1Pressed: palette.navy,
  colorNeutralForeground1Selected: palette.navy,
  colorNeutralForeground2: palette.navy2,
  colorNeutralForeground2Hover: palette.navy,
  colorNeutralForeground2Pressed: palette.navy,
  colorNeutralForeground2Selected: palette.navy,
  colorNeutralForeground3: palette.navy3,
  colorNeutralForeground3Hover: palette.navy2,
  colorNeutralForeground4: palette.navy4,
  colorNeutralBackground1: palette.white,
  colorNeutralBackground2: '#f7f9fb',
  colorNeutralBackground3: palette.lightGrey,
  colorNeutralBackground4: '#eef2f6',
  colorNeutralStroke1: palette.lineStrong,
  colorNeutralStroke2: palette.line,
  colorNeutralStroke3: '#edf1f5',
  colorSubtleBackgroundHover: palette.lightBlue,
  colorSubtleBackgroundPressed: '#d6e6f8',
};
