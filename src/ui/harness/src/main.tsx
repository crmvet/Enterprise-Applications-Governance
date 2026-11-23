// The harness entry ("npm run build", dist/harness): the four pages on fictional fixture JSON, shown in UTC.
// The web app has its own entry, src/app/main.tsx ("npm run build:app", dist/app).
import './styles/globals.css';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { TooltipProvider } from './components/ui/tooltip';
import { configureDisplayZone } from './data/format';

const root = document.getElementById('root');
if (!root) throw new Error('The page has no #root element.');

configureDisplayZone('utc');

createRoot(root).render(
  <StrictMode>
    <TooltipProvider delayDuration={300}>
      <App />
    </TooltipProvider>
  </StrictMode>,
);
