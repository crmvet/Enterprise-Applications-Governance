// The harness entry ("npm run build", dist/harness): the four pages on fictional fixture JSON, shown in UTC.
// The web app has its own entry, src/app/main.tsx ("npm run build:app", dist/app).
import { FluentProvider } from '@fluentui/react-components';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { configureDisplayZone } from './data/format';
import { academyTheme } from './theme';

const root = document.getElementById('root');
if (!root) throw new Error('The page has no #root element.');

configureDisplayZone('utc');

createRoot(root).render(
  <StrictMode>
    <FluentProvider theme={academyTheme} style={{ backgroundColor: 'transparent', minHeight: '100vh' }}>
      <App />
    </FluentProvider>
  </StrictMode>,
);
