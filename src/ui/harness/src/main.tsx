import { FluentProvider } from '@fluentui/react-components';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { academyTheme } from './theme';

const root = document.getElementById('root');
if (!root) throw new Error('The page has no #root element.');

createRoot(root).render(
  <StrictMode>
    <FluentProvider theme={academyTheme} style={{ backgroundColor: 'transparent', minHeight: '100vh' }}>
      <App />
    </FluentProvider>
  </StrictMode>,
);
