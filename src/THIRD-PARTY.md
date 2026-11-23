# Third-party packages

Every package below comes from a public feed: nuget.org for .NET and npmjs.com for Node.
Every version is exact and pinned. Licenses are MIT, Apache 2.0 or BSD (0BSD counts as BSD here), with one open
exception: picocolors (ISC), a build-only package of the page harness (see "Node: ui/harness only").
Licenses were read from each package's own metadata on 2026-11-22.

## Notices in shipped builds

This repository holds no third-party code. Packages are restored from the feeds, and build output (`bin/`, `obj/`,
`node_modules/`, `ui/harness/dist/`) is never committed. MIT, BSD, ISC and Apache 2.0 all ask that the license text
goes with every copy of the package. So any build that is handed to others must carry the license text, and any
notice file, of every package inside it. Two builds would carry packages:

- the Dataverse plug-in package: System.Text.Json and the System packages it needs on net48 (all MIT);
- a built page bundle: React, Fluent UI and their runtime packages (MIT), plus @swc/helpers (Apache 2.0) and tslib (0BSD).

Build-only and test-only packages (xunit, Vite, esbuild, Rollup, postcss, TypeScript) are never shipped.

Where versions live:
- .NET direct versions: `Directory.Packages.props`. Transitive versions: each project's `packages.lock.json`.
- Node versions: `tools/package.json` and `tools/package-lock.json`; for the page harness, `ui/harness/package.json` and `ui/harness/package-lock.json`.

Add a row here for every new package, direct or transitive, in the same change.

## .NET: direct references

| Package | Version | License | Used by | Why |
|---|---|---|---|---|
| Microsoft.NETFramework.ReferenceAssemblies | 1.0.3 | MIT (license URL to the Microsoft/dotnet repo; the package holds no license file) | Every net48 build, build only | Compiles net48 without a .NET Framework developer pack on the machine |
| Microsoft.NETCore.Platforms | 7.0.4 | MIT | Every netstandard2.0 build, build only | Lifts the transitive 1.1.0 from NETStandard.Library, which carries the older Microsoft .NET Library license. Runtime identifier graph only, no code |
| System.Text.Json | 10.0.15 | MIT | Product.Engine.Configuration on net48 and netstandard2.0 | Snapshot and preset JSON. On net10.0 the runtime has it. Packed explicitly into the plug-in package (document 25, section 4.2) |
| xunit.v3 | 4.0.3 | Apache-2.0 | Test projects | Test framework on Microsoft.Testing.Platform |

The SDK also adds NETStandard.Library 2.0.3 (MIT) to every netstandard2.0 build by itself.

## .NET: transitive packages

| Package | Version | License | Comes from |
|---|---|---|---|
| Microsoft.ApplicationInsights | 2.23.0 | MIT | xunit.v3 via the telemetry extension. Never called: `Directory.Build.targets` removes the extension's hook |
| Microsoft.Bcl.AsyncInterfaces | 10.0.15 | MIT | System.Text.Json |
| Microsoft.Bcl.AsyncInterfaces | 6.0.0 | MIT | xunit.v3 |
| Microsoft.NETFramework.ReferenceAssemblies.net48 | 1.0.3 | MIT (license URL to the Microsoft/dotnet repo) | Microsoft.NETFramework.ReferenceAssemblies |
| Microsoft.Testing.Extensions.Telemetry | 2.5.2 | MIT | xunit.v3. Its hook is removed, so it never registers |
| Microsoft.Testing.Extensions.TrxReport.Abstractions | 2.5.2 | MIT | xunit.v3 |
| Microsoft.Testing.Platform | 2.5.2 | MIT | xunit.v3 |
| Microsoft.Testing.Platform.MSBuild | 2.5.2 | MIT | xunit.v3 |
| Microsoft.Win32.Registry | 5.0.0 | MIT | Microsoft.Testing.Platform |
| NETStandard.Library | 2.0.3 | MIT (license file in the package) | The SDK, for netstandard2.0 |
| System.Buffers | 4.6.1 | MIT | System.Text.Json |
| System.Buffers | 4.5.1 | MIT (license file in the package) | xunit.v3 on net48 |
| System.Collections.Immutable | 6.0.0 | MIT | xunit.v3 on net48 |
| System.Diagnostics.DiagnosticSource | 6.0.0 | MIT | xunit.v3 on net48 |
| System.IO.Pipelines | 10.0.15 | MIT | System.Text.Json |
| System.Memory | 4.6.3 | MIT | System.Text.Json |
| System.Memory | 4.5.5 | MIT (license file in the package) | xunit.v3 on net48 |
| System.Numerics.Vectors | 4.6.1 | MIT | System.Memory |
| System.Numerics.Vectors | 4.5.0 | MIT (license file in the package) | xunit.v3 on net48 |
| System.Runtime.CompilerServices.Unsafe | 6.1.2 | MIT | System.Text.Json |
| System.Runtime.CompilerServices.Unsafe | 6.0.0 | MIT | xunit.v3 on net48 |
| System.Security.AccessControl | 6.0.1 | MIT | Microsoft.Win32.Registry |
| System.Security.Principal.Windows | 5.0.0 | MIT | Microsoft.Win32.Registry |
| System.Text.Encodings.Web | 10.0.15 | MIT | System.Text.Json |
| System.Threading.Tasks.Extensions | 4.6.3 | MIT | System.Text.Json |
| System.Threading.Tasks.Extensions | 4.5.4 | MIT (license file in the package) | xunit.v3 on net48 |
| System.ValueTuple | 4.6.2 | MIT | System.Text.Json on net48 |
| xunit.analyzers | 2.3.0 | Apache-2.0 | xunit.v3 |
| xunit.v3.assert | 4.0.3 | Apache-2.0 | xunit.v3 |
| xunit.v3.common | 4.0.3 | Apache-2.0 | xunit.v3 |
| xunit.v3.core.mtp-v2 | 4.0.3 | Apache-2.0 | xunit.v3 (also carries xunit.abstractions.dll for its runners) |
| xunit.v3.extensibility.core | 4.0.3 | Apache-2.0 | xunit.v3 |
| xunit.v3.mtp-v2 | 4.0.3 | Apache-2.0 | xunit.v3 |
| xunit.v3.runner.common | 4.0.3 | Apache-2.0 | xunit.v3 |
| xunit.v3.runner.inproc.console | 4.0.3 | Apache-2.0 | xunit.v3 |

Two versions of some System packages appear because the engine projects and the test projects resolve them separately.
Each project's lock file holds one version per target.

## Node: tools/ only

| Package | Version | License | Direct or transitive | Why |
|---|---|---|---|---|
| ajv | 8.20.0 | MIT | Direct | JSON Schema draft 2020-12 validator for `tools/json-check.mjs` and the record endpoint stub (`samples/pipeline-steps/stub/`) |
| ajv-formats | 3.0.1 | MIT | Direct | Format checks (date, uri and others) for ajv |
| fast-deep-equal | 3.1.3 | MIT | Transitive (ajv) | |
| fast-uri | 3.1.9 | BSD-3-Clause | Transitive (ajv) | |
| json-schema-traverse | 1.0.0 | MIT | Transitive (ajv) | |
| require-from-string | 2.0.2 | MIT | Transitive (ajv) | |

Node packages are installed with `npm ci --ignore-scripts`, so no install scripts run.

The pipeline record step samples add no package. Their stub loads the ajv above from `tools/node_modules`, and
the record step script that clients copy uses Node's built-ins only.

The MCP server core adds no package either. It references only the engine projects; its JSON, schema checking
and protocol handling use the engine's own code. The official MCP C# SDK is not referenced: the library sits behind
`IMcpLibraryAdapter` with a pass-through stub until the MCP library is picked, and gets a row here, pinned, if its license fits.

## Node: ui/harness only

The local page harness: React and Fluent UI v9 pages, built and served by Vite on 127.0.0.1.
Installed with `npm ci --ignore-scripts` in `ui/harness/`. Every package was published on or before 2026-11-08
(the install used `--before=2026-11-08`), so each one was at least two weeks old on 2026-11-22.
No Babel: esbuild compiles the TSX, so the React plugin for Vite and its Babel tree are not used.
`node ui/harness/scripts/check-packages.mjs` checks this table against the lock file (the `ui` check of `tools/ci.mjs`).

Licenses outside MIT, Apache-2.0 and BSD-2-Clause or BSD-3-Clause, still to be confirmed by the product owner:
- **picocolors 1.1.1, ISC.** postcss needs it for terminal colors, and Vite needs postcss. Build time only; it is never
  in the page bundle. ISC is a short permissive license, close to MIT, but it is not on the house list. No Vite
  release drops postcss. If the exception is refused, the harness can move from Vite to esbuild alone (esbuild is MIT).
- **tslib 2.9.1, 0BSD (BSD Zero Clause).** Runtime helpers used by Fluent's styling and icon packages. Counted as BSD here.

| Package | Version | License | Direct or transitive | Use |
|---|---|---|---|---|
| @babel/runtime | 7.29.13 | MIT | Transitive (rtl-css-js) | Runtime dependency |
| @emotion/hash | 0.9.2 | MIT | Transitive (@griffel/core) | Runtime dependency |
| @esbuild/aix-ppc64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/android-arm | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/android-arm64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/android-x64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/darwin-arm64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/darwin-x64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/freebsd-arm64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/freebsd-x64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/linux-arm | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/linux-arm64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/linux-ia32 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/linux-loong64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/linux-mips64el | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/linux-ppc64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/linux-riscv64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/linux-s390x | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/linux-x64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/netbsd-arm64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/netbsd-x64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/openbsd-arm64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/openbsd-x64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/openharmony-arm64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/sunos-x64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/win32-arm64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/win32-ia32 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary for another system; not installed here |
| @esbuild/win32-x64 | 0.28.2 | MIT | Transitive (esbuild) | Build only. Optional platform binary, the one installed on Windows x64 |
| @floating-ui/core | 1.8.0 | MIT | Transitive (@floating-ui/dom) | Runtime dependency |
| @floating-ui/devtools | 0.2.3 | MIT | Transitive (@fluentui/react-positioning) | Runtime dependency |
| @floating-ui/dom | 1.8.0 | MIT | Transitive (@floating-ui/devtools) | Runtime dependency |
| @floating-ui/utils | 0.2.12 | MIT | Transitive (@floating-ui/core) | Runtime dependency |
| @fluentui/keyboard-keys | 9.0.9 | MIT | Transitive (@fluentui/react-aria) | Runtime dependency |
| @fluentui/priority-overflow | 9.4.3 | MIT | Transitive (@fluentui/react-overflow) | Runtime dependency |
| @fluentui/react-accordion | 9.12.5 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-alert | 9.0.2-beta.1 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-aria | 9.17.16 | MIT | Transitive (@fluentui/react-accordion) | Runtime dependency |
| @fluentui/react-avatar | 9.11.9 | MIT | Transitive (@fluentui/react-alert) | Runtime dependency |
| @fluentui/react-badge | 9.5.7 | MIT | Transitive (@fluentui/react-avatar) | Runtime dependency |
| @fluentui/react-breadcrumb | 9.4.8 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-button | 9.11.3 | MIT | Transitive (@fluentui/react-alert) | Runtime dependency |
| @fluentui/react-card | 9.7.4 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-carousel | 9.9.16 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-checkbox | 9.6.6 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-color-picker | 9.3.3 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-combobox | 9.17.8 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-components | 9.75.1 | MIT | Direct | Runtime dependency. Fluent UI v9 components, tokens and the light theme of the four pages |
| @fluentui/react-context-selector | 9.2.21 | MIT | Transitive (@fluentui/react-accordion) | Runtime dependency |
| @fluentui/react-dialog | 9.18.6 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-divider | 9.7.6 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-drawer | 9.13.5 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-field | 9.5.6 | MIT | Transitive (@fluentui/react-checkbox) | Runtime dependency |
| @fluentui/react-icons | 2.0.345 | MIT | Direct | Runtime dependency. Fluent icons on the pages |
| @fluentui/react-image | 9.4.6 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-infobutton | 9.0.1-beta.1 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-infolabel | 9.4.28 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-input | 9.8.8 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-jsx-runtime | 9.4.7 | MIT | Transitive (@fluentui/react-accordion) | Runtime dependency |
| @fluentui/react-label | 9.4.6 | MIT | Transitive (@fluentui/react-checkbox) | Runtime dependency |
| @fluentui/react-link | 9.8.6 | MIT | Transitive (@fluentui/react-breadcrumb) | Runtime dependency |
| @fluentui/react-list | 9.7.0 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-menu | 9.25.8 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-message-bar | 9.7.9 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-motion | 9.16.5 | MIT | Transitive (@fluentui/react-accordion) | Runtime dependency |
| @fluentui/react-motion-components-preview | 0.15.10 | MIT | Transitive (@fluentui/react-accordion) | Runtime dependency |
| @fluentui/react-nav | 9.4.9 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-overflow | 9.9.5 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-persona | 9.7.11 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-popover | 9.14.10 | MIT | Transitive (@fluentui/react-avatar) | Runtime dependency |
| @fluentui/react-portal | 9.8.17 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-positioning | 9.23.4 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-progress | 9.5.7 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-provider | 9.22.23 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-radio | 9.6.7 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-rating | 9.4.7 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-search | 9.4.8 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-select | 9.5.7 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-shared-contexts | 9.26.4 | MIT | Transitive (@fluentui/react-accordion) | Runtime dependency |
| @fluentui/react-skeleton | 9.7.7 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-slider | 9.6.8 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-spinbutton | 9.6.8 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-spinner | 9.8.7 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-swatch-picker | 9.6.3 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-switch | 9.7.7 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-table | 9.19.23 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-tabs | 9.12.6 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-tabster | 9.26.19 | MIT | Transitive (@fluentui/react-accordion) | Runtime dependency |
| @fluentui/react-tag-picker | 9.10.7 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-tags | 9.9.8 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-teaching-popover | 9.7.9 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-text | 9.6.21 | MIT | Transitive (@fluentui/react-card) | Runtime dependency |
| @fluentui/react-textarea | 9.7.8 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-theme | 9.2.2 | MIT | Transitive (@fluentui/react-accordion) | Runtime dependency |
| @fluentui/react-toast | 9.8.5 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-toolbar | 9.8.7 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-tooltip | 9.10.8 | MIT | Transitive (@fluentui/react-avatar) | Runtime dependency |
| @fluentui/react-tree | 9.17.0 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/react-utilities | 9.27.0 | MIT | Transitive (@fluentui/react-accordion) | Runtime dependency |
| @fluentui/react-virtualizer | 9.0.0-alpha.116 | MIT | Transitive (@fluentui/react-components) | Runtime dependency |
| @fluentui/tokens | 1.0.0-alpha.24 | MIT | Transitive (@fluentui/react-theme) | Runtime dependency |
| @griffel/core | 1.21.4 | MIT | Transitive (@fluentui/react-provider) | Runtime dependency |
| @griffel/react | 1.7.8 | MIT | Transitive (@fluentui/react-accordion) | Runtime dependency |
| @griffel/style-types | 1.4.3 | MIT | Transitive (@griffel/core) | Runtime dependency |
| @rollup/rollup-android-arm-eabi | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-android-arm64 | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-android-x64 | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-darwin-arm64 | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-darwin-x64 | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-freebsd-arm64 | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-freebsd-x64 | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-linux-arm-gnueabihf | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-linux-arm-musleabihf | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-linux-arm64-gnu | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-linux-arm64-musl | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-linux-loong64-gnu | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-linux-loong64-musl | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-linux-ppc64-gnu | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-linux-ppc64-musl | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-linux-riscv64-gnu | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-linux-riscv64-musl | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-linux-s390x-gnu | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-linux-x64-gnu | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-linux-x64-musl | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-openbsd-x64 | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-openharmony-arm64 | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-win32-arm64-msvc | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-win32-ia32-msvc | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| @rollup/rollup-win32-x64-gnu | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary, the one installed on Windows x64 |
| @rollup/rollup-win32-x64-msvc | 4.68.0 | MIT | Transitive (rollup) | Build only. Optional platform binary, the one installed on Windows x64 |
| @swc/helpers | 0.5.24 | Apache-2.0 | Transitive (@fluentui/keyboard-keys) | Runtime dependency |
| @types/estree | 1.0.10 | MIT | Transitive (rollup) | Types only, never in the bundle |
| @types/prop-types | 15.7.15 | MIT | Transitive (@types/react) | Types only, never in the bundle |
| @types/react | 18.3.31 | MIT | Direct | Types only, never in the bundle. Types for the typecheck |
| @types/react-dom | 18.3.7 | MIT | Direct | Types only, never in the bundle. Types for the typecheck |
| csstype | 3.2.3 | MIT | Transitive (@griffel/core) | Types only, never in the bundle |
| embla-carousel | 8.6.0 | MIT | Transitive (@fluentui/react-carousel) | Runtime dependency |
| embla-carousel-autoplay | 8.6.0 | MIT | Transitive (@fluentui/react-carousel) | Runtime dependency |
| embla-carousel-fade | 8.6.0 | MIT | Transitive (@fluentui/react-carousel) | Runtime dependency |
| esbuild | 0.28.2 | MIT | Transitive (vite) | Build only |
| fdir | 6.5.0 | MIT | Transitive (tinyglobby) | Build only |
| fsevents | 2.3.3 | MIT | Transitive (rollup) | Build only. Optional platform binary for another system; not installed here |
| js-tokens | 4.0.0 | MIT | Transitive (loose-envify) | Runtime dependency |
| keyborg | 3.498.0 | MIT | Transitive (@fluentui/react-tabster) | Runtime dependency |
| loose-envify | 1.4.0 | MIT | Transitive (react) | Runtime dependency |
| nanoid | 3.3.20 | MIT | Transitive (postcss) | Build only |
| picocolors | 1.1.1 | ISC (exception, open question: build time only) | Transitive (postcss) | Build only |
| picomatch | 4.0.7 | MIT | Transitive (fdir) | Build only |
| postcss | 8.5.32 | MIT | Transitive (vite) | Build only |
| react | 18.3.1 | MIT | Direct | Runtime dependency. UI runtime |
| react-dom | 18.3.1 | MIT | Direct | Runtime dependency. UI runtime in the browser |
| rollup | 4.68.0 | MIT | Transitive (vite) | Build only |
| rtl-css-js | 1.16.1 | MIT | Transitive (@griffel/core) | Runtime dependency |
| scheduler | 0.23.2 | MIT | Transitive (react-dom) | Runtime dependency |
| scheduler | 0.28.0 | MIT | Transitive (@fluentui/react-context-selector, as a peer) | Runtime dependency |
| source-map-js | 1.2.2 | BSD-3-Clause | Transitive (postcss) | Build only |
| stylis | 4.4.0 | MIT | Transitive (@griffel/core) | Runtime dependency |
| tabster | 8.8.1 | MIT | Transitive (@fluentui/react-tabster) | Runtime dependency |
| tinyglobby | 0.2.17 | MIT | Transitive (vite) | Build only |
| tslib | 2.9.1 | 0BSD (BSD Zero Clause) | Transitive (@fluentui/react-icons) | Runtime dependency |
| typescript | 5.9.3 | Apache-2.0 | Direct | Build only. Typecheck (tsc --noEmit). The JavaScript compiler, no native binary |
| use-sync-external-store | 1.7.0 | MIT | Transitive (@fluentui/react-positioning) | Runtime dependency |
| vite | 7.3.7 | MIT | Direct | Build only. Local build, and the preview server on 127.0.0.1 for the screenshots |
