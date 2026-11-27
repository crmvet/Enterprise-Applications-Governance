// What the install is, for the pages that differ by install: a demo install (D-33) holds the fictional demo estate and
// shows the Demo guide; in the Demo environment config.json also lists the demo personas, and the switch (D-39) lets
// the operator act as one of them. The shell provides it; elsewhere it reads as a plain install.
// Wave 4: on a local run with the development sign-in, devPersonas switches the development identity through the same
// interface (demoPersona.ts devPersonaSwitch), so the Demo guide's persona links work there too.
// It sends no header of its own: the development sign-in already names the persona on every call.
import { createContext, useContext } from 'react';
import type { DemoPersonaSwitch } from './demoPersona';

export interface Install {
  demo: boolean;
  // The demo persona switch, or null when the install lists no demo personas.
  personas: DemoPersonaSwitch | null;
  // The development identity as a persona switch, on a local run with the development sign-in; else null.
  devPersonas: DemoPersonaSwitch | null;
}

export const InstallContext = createContext<Install>({ demo: false, personas: null, devPersonas: null });

export function useInstall(): Install {
  return useContext(InstallContext);
}
