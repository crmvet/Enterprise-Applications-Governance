# Third-party packages

Every package below comes from a public feed: nuget.org for .NET and npmjs.com for Node.
Every version is exact and pinned. Licenses are MIT, Apache 2.0 or BSD (0BSD counts as BSD here), with these
exceptions:
- lucide-react (ISC), the icons in the web app's page bundle, and three build-only packages of the web app:
  picocolors and graceful-fs (ISC) and lightningcss with its platform binaries (MPL-2.0). All four are still open for
  the product owner (see "Node: ui/harness only");
- two transitive Microsoft packages of the Azure adapter under the Microsoft Software License Terms,
  Microsoft.Data.SqlClient.SNI.runtime and Microsoft.Identity.Client.NativeInterop. They are restored from NuGet
  under Microsoft's terms and are not part of this repository's MIT-licensed source (see ".NET: adapters/azure").

Licenses were read from each package's own metadata on 2026-11-22, and for the Azure adapter on 2026-11-23.

## Notices in shipped builds

This repository holds one piece of third-party source: the shadcn/ui components and shadcn's Tailwind helper CSS
(MIT), copied into `ui/harness/src/components/ui/` and `ui/harness/src/styles/shadcn-tailwind.css`, with their license
text in `ui/harness/src/components/ui/README.md`. Everything else is restored from the feeds, and build output (`bin/`, `obj/`,
`node_modules/`, `ui/harness/dist/`) is never committed. MIT, BSD, ISC and Apache 2.0 all ask that the license text
goes with every copy of the package. So any build that is handed to others must carry the license text, and any
notice file, of every package inside it. Four builds would carry packages:

- the Dataverse plug-in package: System.Text.Json and the System packages it needs on net48 (all MIT);
- a built page bundle (the harness or the web app): React, the Radix primitives, the shadcn/ui components and their
  runtime packages (MIT), lucide-react (ISC), class-variance-authority (Apache 2.0) and tslib (0BSD), in the web app also
  MSAL for the browser (@azure/msal-browser and @azure/msal-common, MIT);
- the Azure adapter's Functions deployment package: the packages marked "Functions app" in the column "In" of both
  tables under ".NET: adapters/azure", including the two Microsoft packages listed there under their own terms, and,
  because the package carries the built web app under `wwwroot/app`, the packages of the web app's page bundle above;
- the Azure adapter's server container image: the packages marked "Server" there, including the same two, on top of
  the .NET base image, which carries its own notices.

Build-only and test-only packages (xunit, Vite, esbuild, Rollup, postcss, Tailwind CSS and lightningcss, TypeScript) are
never shipped.

Where versions live:
- .NET direct versions: `Directory.Packages.props`, plus `adapters/azure/Directory.Packages.props` for the Azure
  adapter (it imports the one in `src/` first). Transitive versions: each project's `packages.lock.json`.
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

## .NET: adapters/azure

The Azure adapter (`adapters/azure/`, with its own solution `Product.Azure.sln`) adds the packages below, direct and
transitive, that the sections above do not already list at the same version.

- Direct versions are pinned in `adapters/azure/Directory.Packages.props`, which imports `Directory.Packages.props`
  in `src/` first. Transitive versions come from each adapter project's `packages.lock.json`.
- Licenses were read from each package's own metadata (the license expression in its nuspec, or the license file it
  carries) on 2026-11-23.
- Every direct version is a stable release from nuget.org that was at least two weeks old when it was picked.
- The xUnit, Microsoft.Testing and System packages that the adapter's test projects share with the rest of `src/`
  are listed above and not repeated here.

Two builds of the adapter would carry packages: the Azure Functions deployment package (`Product.Azure.Functions`,
built by `azd deploy`) and the server container image (`Product.Azure.Server/Dockerfile`). The column "In" of both
tables below says which of them carries each package. "Library and test builds only" means a version that a
library or test project restores but neither of those builds carries, because the host resolves a newer version of
the same package. "Build only" and "Tests only" packages are in neither.

### Licenses outside MIT, Apache 2.0 and BSD

Two transitive Microsoft packages carry the Microsoft Software License Terms. The adapter does not reference either
one directly. Both are restored from NuGet under Microsoft's terms and are not part of this repository's MIT-licensed
source: the repository holds no copy of them, and its MIT license does not cover them.

- **Microsoft.Data.SqlClient.SNI.runtime 7.1.0, Microsoft Software License Terms.** The native SQL Server network
  layer that Microsoft.Data.SqlClient uses on Windows (`runtimes/win-*/native/Microsoft.Data.SqlClient.SNI.dll`).
  Microsoft.Data.SqlClient depends on it on every platform, so every .NET application that talks to Azure SQL with
  Microsoft.Data.SqlClient restores it. On Linux, where the Functions app and the container run, SqlClient uses its
  managed network layer and never loads the file, but a portable build still carries the Windows folders; a build
  published for `linux-x64` leaves them out. Its terms (the `LICENSE.txt` inside the package) allow the object code to
  be distributed inside an application (as "Distributable Code"), not on its own: "use the Distributable Code in your
  applications and not as a standalone distribution".
- **Microsoft.Identity.Client.NativeInterop 0.20.6, Microsoft Software License Terms.** The native runtime of the
  Windows sign-in broker (`runtimes/*/native/msalruntime*`) and its managed wrapper
  (`Microsoft.Identity.Client.NativeInterop.dll`), brought by Microsoft.Identity.Client.Broker 4.84.2,
  which Microsoft.Data.SqlClient.Extensions.Azure 7.1.1 references for interactive sign-in. The product never uses
  interactive or broker sign-in: the app connects with its managed identity and the migration with the deployer's
  default credential. Its terms (the `LICENSE` file inside the package) grant use and say you may not "share,
  publish, distribute, or lease the software", so a deployment package or image built from this source carries files
  that may not be handed on. Each deployment is
  built from source into its own subscription (the azd template builds on the deployer's machine). Giving SqlClient
  its token through `SqlConnection.AccessTokenCallback` with an Azure.Identity credential, in place of
  Microsoft.Data.SqlClient.Extensions.Azure, would remove this package from every build.

### Direct references

| Package | Version | License | Used by | Why | In |
|---|---|---|---|---|---|
| Microsoft.Identity.Web | 4.18.0 | MIT | Product.Azure.Host | Microsoft Entra ID JWT bearer validation with app roles | Functions app, server |
| Microsoft.Data.Sqlite | 10.0.14 | MIT | Product.Azure.Stores.Sqlite | The SQLite store for local runs, demos and tests | Server |
| Microsoft.Data.SqlClient | 7.1.1 | MIT | Product.Azure.Stores.SqlServer | The Azure SQL store | Functions app, server |
| Microsoft.Data.SqlClient.Extensions.Azure | 7.1.1 | MIT | Product.Azure.Stores.SqlServer | Microsoft Entra authentication for SqlClient (managed identity, default credential). Brings the broker runtime listed under "Licenses outside MIT, Apache 2.0 and BSD" | Functions app, server |
| System.Security.Cryptography.Pkcs | 10.0.14 | MIT | Product.Azure.Stores.SqlServer | Lifts the vulnerable 9.0.18 that Microsoft.Data.SqlClient asks for | Functions app, server |
| Azure.Identity | 1.21.0 | MIT | Product.Azure.Functions | The managed identity credential of the telemetry exporter; one home for the credential types | Functions app |
| Azure.Functions.Sdk | 1.0.1 | MIT | Product.Azure.Functions | The project SDK of the Functions wrapper (pinned in the project file's `Sdk` attribute) | Functions app: one file, the function metadata loader in `.azurefunctions/` |
| Microsoft.Azure.Functions.Worker | 2.52.0 | MIT | Product.Azure.Functions | The isolated worker | Functions app |
| Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore | 2.1.1 | MIT | Product.Azure.Functions | ASP.NET Core integration for the HTTP trigger | Functions app |
| Microsoft.Azure.Functions.Worker.Extensions.Timer | 4.3.1 | MIT | Product.Azure.Functions | The timer trigger of the engine's daily due-state refresh | Functions app |
| Microsoft.Azure.Functions.Worker.OpenTelemetry | 1.2.0 | MIT | Product.Azure.Functions | Worker telemetry through OpenTelemetry | Functions app |
| Microsoft.Azure.Functions.Worker.Sdk.Analyzers | 1.2.2 | MIT | Product.Azure.Functions | Added by the Azure.Functions.Sdk project SDK | Build only |
| Microsoft.Azure.Functions.Worker.Sdk.Generators | 1.4.0 | MIT | Product.Azure.Functions | Added by the Azure.Functions.Sdk project SDK | Build only |
| Azure.Monitor.OpenTelemetry.Exporter | 1.10.0 | MIT | Product.Azure.Functions | Telemetry to Application Insights, only when a connection string is set | Functions app |
| Azure.Monitor.OpenTelemetry.AspNetCore | 1.6.0 | MIT | Product.Azure.Server | Telemetry to Application Insights, only when a connection string is set | Server |
| ModelContextProtocol | 2.4.0 | Apache-2.0 | Product.Azure.Tests | The official MCP C# SDK client that proves a third-party client speaks to the endpoint. The hosts get it too, through ModelContextProtocol.AspNetCore | Functions app, server |
| ModelContextProtocol.AspNetCore | 2.4.0 | Apache-2.0 | Product.Azure.Host | The SDK server transport, chosen with `Product:Mcp:Transport` = `sdk` | Functions app, server |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.14 | MIT | Product.Azure.Tests | The in-memory HTTP test server | Tests only |

The .NET 10 shared frameworks (Microsoft.NETCore.App, Microsoft.AspNetCore.App) come with the SDK and the runtime, not
from packages.

### Transitive packages

Some packages appear at two or more versions because each project resolves its own graph. Each lock file holds one
version per package.

| Package | Version | License | Comes from | In |
|---|---|---|---|---|
| Azure.Core | 1.50.0 | MIT | Azure.Identity | Library and test builds only |
| Azure.Core | 1.51.1 | MIT | Azure.Identity | Library and test builds only |
| Azure.Core | 1.60.0 | MIT | Azure.Identity | Server |
| Azure.Core | 1.63.0 | MIT | Azure.Identity | Functions app |
| Azure.Identity | 1.17.2 | MIT | Microsoft.Identity.Web.Certificate | Library and test builds only |
| Azure.Identity | 1.18.0 | MIT | Microsoft.Data.SqlClient.Extensions.Azure | Server |
| Azure.Monitor.OpenTelemetry.Exporter | 1.8.3 | MIT | Azure.Monitor.OpenTelemetry.AspNetCore | Server |
| Azure.Security.KeyVault.Certificates | 4.6.0 | MIT | Microsoft.Identity.Web.Certificate | Functions app, server |
| Azure.Security.KeyVault.Secrets | 4.6.0 | MIT | Microsoft.Identity.Web.Certificate | Functions app, server |
| Google.Protobuf | 3.28.0 | BSD-3-Clause | Microsoft.Azure.Functions.Worker.Grpc | Functions app |
| Grpc.Core.Api | 2.65.0 | Apache-2.0 | Grpc.Net.Common | Functions app |
| Grpc.Net.Client | 2.65.0 | Apache-2.0 | Grpc.Net.ClientFactory | Functions app |
| Grpc.Net.ClientFactory | 2.65.0 | Apache-2.0 | Microsoft.Azure.Functions.Worker.Grpc | Functions app |
| Grpc.Net.Common | 2.65.0 | Apache-2.0 | Grpc.Net.Client | Functions app |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.13 | MIT | Microsoft.Identity.Web.TokenAcquisition | Functions app, server |
| Microsoft.AspNetCore.Authentication.OpenIdConnect | 10.0.13 | MIT | Microsoft.Identity.Web.TokenAcquisition | Functions app, server |
| Microsoft.AspNetCore.Cryptography.Internal | 10.0.13 | MIT | Microsoft.AspNetCore.DataProtection | Functions app |
| Microsoft.AspNetCore.DataProtection | 10.0.13 | MIT | Microsoft.Identity.Web.TokenCache | Functions app |
| Microsoft.AspNetCore.DataProtection.Abstractions | 10.0.13 | MIT | Microsoft.AspNetCore.DataProtection | Functions app |
| Microsoft.AspNetCore.TestHost | 10.0.14 | MIT | Microsoft.AspNetCore.Mvc.Testing | Tests only |
| Microsoft.Azure.Functions.Worker.Core | 2.52.0 | MIT | Microsoft.Azure.Functions.Worker | Functions app |
| Microsoft.Azure.Functions.Worker.Extensions.Abstractions | 1.3.0 | MIT | Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore | Functions app |
| Microsoft.Azure.Functions.Worker.Extensions.Http | 3.3.0 | MIT | Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore | Functions app |
| Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore.Analyzers | 1.0.4 | MIT | Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore | Functions app |
| Microsoft.Azure.Functions.Worker.Grpc | 2.52.0 | MIT | Microsoft.Azure.Functions.Worker | Functions app |
| Microsoft.Bcl.AsyncInterfaces | 8.0.0 | MIT | Azure.Core | Library and test builds only |
| Microsoft.Bcl.AsyncInterfaces | 10.0.2 | MIT | Azure.Core | Library and test builds only |
| Microsoft.Bcl.AsyncInterfaces | 10.0.9 | MIT | Azure.Core | Server |
| Microsoft.Bcl.AsyncInterfaces | 10.0.10 | MIT | Azure.Core | Functions app |
| Microsoft.Bcl.Cryptography | 9.0.18 | MIT | Microsoft.Data.SqlClient | Library and test builds only |
| Microsoft.Bcl.Cryptography | 10.0.2 | MIT | Microsoft.Data.SqlClient | Functions app, server |
| Microsoft.Data.SqlClient.Extensions.Abstractions | 7.1.1 | MIT | Microsoft.Data.SqlClient | Functions app, server |
| Microsoft.Data.SqlClient.Internal.Logging | 7.1.1 | MIT | Microsoft.Data.SqlClient | Functions app, server |
| Microsoft.Data.SqlClient.SNI.runtime | 7.1.0 | Microsoft Software License Terms (see "Licenses outside MIT, Apache 2.0 and BSD") | Microsoft.Data.SqlClient | Functions app, server |
| Microsoft.Data.Sqlite.Core | 10.0.14 | MIT | Microsoft.Data.Sqlite | Server |
| Microsoft.Extensions.AI.Abstractions | 10.11.0 | MIT | ModelContextProtocol.Core | Functions app, server |
| Microsoft.Extensions.Caching.Abstractions | 9.0.18 | MIT | Microsoft.Extensions.Caching.Memory | Library and test builds only |
| Microsoft.Extensions.Caching.Abstractions | 10.0.14 | MIT | ModelContextProtocol | Functions app |
| Microsoft.Extensions.Caching.Memory | 9.0.18 | MIT | Microsoft.Data.SqlClient | Library and test builds only |
| Microsoft.Extensions.Caching.Memory | 10.0.0 | MIT | Microsoft.Data.SqlClient | Functions app |
| Microsoft.Extensions.Configuration | 10.0.0 | MIT | Microsoft.Extensions.Configuration.Binder | Functions app |
| Microsoft.Extensions.Configuration | 10.0.14 | MIT | Microsoft.Extensions.Configuration.Binder | Tests only |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.3 | MIT | Azure.Identity | Library and test builds only |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.14 | MIT | Azure.Identity | Functions app |
| Microsoft.Extensions.Configuration.Binder | 10.0.0 | MIT | Microsoft.Extensions.Hosting | Functions app |
| Microsoft.Extensions.Configuration.Binder | 10.0.14 | MIT | Microsoft.Extensions.Hosting | Tests only |
| Microsoft.Extensions.Configuration.CommandLine | 10.0.0 | MIT | Microsoft.Extensions.Hosting | Functions app |
| Microsoft.Extensions.Configuration.CommandLine | 10.0.14 | MIT | Microsoft.Extensions.Hosting | Tests only |
| Microsoft.Extensions.Configuration.EnvironmentVariables | 10.0.0 | MIT | Microsoft.Extensions.Hosting | Functions app |
| Microsoft.Extensions.Configuration.EnvironmentVariables | 10.0.14 | MIT | Microsoft.Extensions.Hosting | Tests only |
| Microsoft.Extensions.Configuration.FileExtensions | 10.0.0 | MIT | Microsoft.Extensions.Configuration.Json | Functions app |
| Microsoft.Extensions.Configuration.FileExtensions | 10.0.14 | MIT | Microsoft.Extensions.Configuration.Json | Tests only |
| Microsoft.Extensions.Configuration.Json | 10.0.0 | MIT | Microsoft.Extensions.Configuration.UserSecrets | Functions app |
| Microsoft.Extensions.Configuration.Json | 10.0.14 | MIT | Microsoft.Extensions.Configuration.UserSecrets | Tests only |
| Microsoft.Extensions.Configuration.UserSecrets | 10.0.0 | MIT | Microsoft.Extensions.Hosting | Functions app |
| Microsoft.Extensions.Configuration.UserSecrets | 10.0.14 | MIT | Microsoft.Extensions.Hosting | Tests only |
| Microsoft.Extensions.DependencyInjection | 10.0.0 | MIT | Microsoft.Extensions.Hosting | Functions app |
| Microsoft.Extensions.DependencyInjection | 10.0.14 | MIT | Microsoft.Extensions.Hosting | Tests only |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.3 | MIT | Microsoft.Extensions.Caching.Memory | Library and test builds only |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.14 | MIT | Microsoft.AspNetCore.DataProtection | Functions app |
| Microsoft.Extensions.DependencyModel | 10.0.14 | MIT | Microsoft.AspNetCore.Mvc.Testing | Tests only |
| Microsoft.Extensions.Diagnostics | 10.0.0 | MIT | Microsoft.Extensions.Hosting | Functions app |
| Microsoft.Extensions.Diagnostics | 10.0.14 | MIT | Microsoft.Extensions.Hosting | Tests only |
| Microsoft.Extensions.Diagnostics.Abstractions | 10.0.3 | MIT | Microsoft.Extensions.Hosting.Abstractions | Library and test builds only |
| Microsoft.Extensions.Diagnostics.Abstractions | 10.0.14 | MIT | Microsoft.Extensions.Diagnostics | Functions app |
| Microsoft.Extensions.FileProviders.Abstractions | 10.0.3 | MIT | Microsoft.Extensions.Hosting.Abstractions | Library and test builds only |
| Microsoft.Extensions.FileProviders.Abstractions | 10.0.14 | MIT | Microsoft.Extensions.Configuration.FileExtensions | Functions app |
| Microsoft.Extensions.FileProviders.Physical | 10.0.0 | MIT | Microsoft.Extensions.Configuration.FileExtensions | Functions app |
| Microsoft.Extensions.FileProviders.Physical | 10.0.14 | MIT | Microsoft.Extensions.Configuration.FileExtensions | Tests only |
| Microsoft.Extensions.FileSystemGlobbing | 10.0.0 | MIT | Microsoft.Extensions.FileProviders.Physical | Functions app |
| Microsoft.Extensions.FileSystemGlobbing | 10.0.14 | MIT | Microsoft.Extensions.FileProviders.Physical | Tests only |
| Microsoft.Extensions.Hosting | 10.0.0 | MIT | Microsoft.Azure.Functions.Worker.Core | Functions app |
| Microsoft.Extensions.Hosting | 10.0.14 | MIT | Microsoft.AspNetCore.Mvc.Testing | Tests only |
| Microsoft.Extensions.Hosting.Abstractions | 10.0.3 | MIT | Azure.Identity | Library and test builds only |
| Microsoft.Extensions.Hosting.Abstractions | 10.0.14 | MIT | Azure.Identity | Functions app |
| Microsoft.Extensions.Http | 6.0.0 | MIT | Grpc.Net.ClientFactory | Functions app |
| Microsoft.Extensions.Logging | 10.0.0 | MIT | Microsoft.Extensions.Hosting | Functions app |
| Microsoft.Extensions.Logging | 10.0.14 | MIT | Microsoft.Extensions.Hosting | Tests only |
| Microsoft.Extensions.Logging.Abstractions | 10.0.3 | MIT | Microsoft.Extensions.Caching.Memory | Library and test builds only |
| Microsoft.Extensions.Logging.Abstractions | 10.0.14 | MIT | Grpc.Net.Client | Functions app |
| Microsoft.Extensions.Logging.Configuration | 10.0.0 | MIT | Microsoft.Extensions.Hosting | Functions app |
| Microsoft.Extensions.Logging.Configuration | 10.0.14 | MIT | Microsoft.Extensions.Hosting | Tests only |
| Microsoft.Extensions.Logging.Console | 10.0.0 | MIT | Microsoft.Extensions.Hosting | Functions app |
| Microsoft.Extensions.Logging.Console | 10.0.14 | MIT | Microsoft.Extensions.Hosting | Tests only |
| Microsoft.Extensions.Logging.Debug | 10.0.0 | MIT | Microsoft.Extensions.Hosting | Functions app |
| Microsoft.Extensions.Logging.Debug | 10.0.14 | MIT | Microsoft.Extensions.Hosting | Tests only |
| Microsoft.Extensions.Logging.EventLog | 10.0.0 | MIT | Microsoft.Extensions.Hosting | Functions app |
| Microsoft.Extensions.Logging.EventLog | 10.0.14 | MIT | Microsoft.Extensions.Hosting | Tests only |
| Microsoft.Extensions.Logging.EventSource | 10.0.0 | MIT | Microsoft.Extensions.Hosting | Functions app |
| Microsoft.Extensions.Logging.EventSource | 10.0.14 | MIT | Microsoft.Extensions.Hosting | Tests only |
| Microsoft.Extensions.Options | 10.0.3 | MIT | Microsoft.Extensions.Caching.Memory | Library and test builds only |
| Microsoft.Extensions.Options | 10.0.14 | MIT | Microsoft.AspNetCore.DataProtection | Functions app |
| Microsoft.Extensions.Options.ConfigurationExtensions | 10.0.0 | MIT | Microsoft.Extensions.Diagnostics | Functions app |
| Microsoft.Extensions.Options.ConfigurationExtensions | 10.0.14 | MIT | Microsoft.Extensions.Diagnostics | Tests only |
| Microsoft.Extensions.Primitives | 10.0.3 | MIT | Microsoft.Extensions.Caching.Abstractions | Library and test builds only |
| Microsoft.Extensions.Primitives | 10.0.14 | MIT | Microsoft.Extensions.Caching.Abstractions | Functions app |
| Microsoft.Identity.Abstractions | 12.7.0 | MIT | Microsoft.Identity.Web.Certificate | Functions app, server |
| Microsoft.Identity.Client | 4.84.2 | MIT | Azure.Identity | Library and test builds only |
| Microsoft.Identity.Client | 4.90.1 | MIT | Azure.Identity | Functions app, server |
| Microsoft.Identity.Client.Broker | 4.84.2 | MIT | Microsoft.Data.SqlClient.Extensions.Azure | Functions app, server |
| Microsoft.Identity.Client.Extensions.Msal | 4.78.0 | MIT | Azure.Identity | Library and test builds only |
| Microsoft.Identity.Client.Extensions.Msal | 4.83.1 | MIT | Azure.Identity | Library and test builds only |
| Microsoft.Identity.Client.Extensions.Msal | 4.84.2 | MIT | Azure.Identity | Server |
| Microsoft.Identity.Client.Extensions.Msal | 4.90.0 | MIT | Azure.Core | Functions app |
| Microsoft.Identity.Client.NativeInterop | 0.20.6 | Microsoft Software License Terms (see "Licenses outside MIT, Apache 2.0 and BSD") | Microsoft.Identity.Client.Broker | Functions app, server |
| Microsoft.Identity.Web.Certificate | 4.18.0 | MIT | Microsoft.Identity.Web | Functions app, server |
| Microsoft.Identity.Web.Certificateless | 4.18.0 | MIT | Microsoft.Identity.Web | Functions app, server |
| Microsoft.Identity.Web.Diagnostics | 4.18.0 | MIT | Microsoft.Identity.Web.Certificate | Functions app, server |
| Microsoft.Identity.Web.TokenAcquisition | 4.18.0 | MIT | Microsoft.Identity.Web | Functions app, server |
| Microsoft.Identity.Web.TokenCache | 4.18.0 | MIT | Microsoft.Identity.Web | Functions app, server |
| Microsoft.IdentityModel.Abstractions | 8.16.0 | MIT | Microsoft.Identity.Client | Library and test builds only |
| Microsoft.IdentityModel.Abstractions | 8.23.0 | MIT | Microsoft.Identity.Client | Functions app, server |
| Microsoft.IdentityModel.JsonWebTokens | 8.16.0 | MIT | Microsoft.Data.SqlClient | Library and test builds only |
| Microsoft.IdentityModel.JsonWebTokens | 8.23.0 | MIT | Microsoft.Data.SqlClient | Functions app, server |
| Microsoft.IdentityModel.Logging | 8.16.0 | MIT | Microsoft.IdentityModel.Tokens | Library and test builds only |
| Microsoft.IdentityModel.Logging | 8.23.0 | MIT | Microsoft.Identity.Web.TokenAcquisition | Functions app, server |
| Microsoft.IdentityModel.LoggingExtensions | 8.23.0 | MIT | Microsoft.Identity.Web.Certificateless | Functions app, server |
| Microsoft.IdentityModel.Protocols | 8.16.0 | MIT | Microsoft.IdentityModel.Protocols.OpenIdConnect | Library and test builds only |
| Microsoft.IdentityModel.Protocols | 8.23.0 | MIT | Microsoft.IdentityModel.Protocols.OpenIdConnect | Functions app, server |
| Microsoft.IdentityModel.Protocols.OpenIdConnect | 8.16.0 | MIT | Microsoft.Data.SqlClient | Library and test builds only |
| Microsoft.IdentityModel.Protocols.OpenIdConnect | 8.23.0 | MIT | Microsoft.Data.SqlClient | Functions app, server |
| Microsoft.IdentityModel.Tokens | 8.16.0 | MIT | Microsoft.IdentityModel.JsonWebTokens | Library and test builds only |
| Microsoft.IdentityModel.Tokens | 8.23.0 | MIT | Microsoft.IdentityModel.JsonWebTokens | Functions app, server |
| Microsoft.IdentityModel.Validators | 8.23.0 | MIT | Microsoft.Identity.Web | Functions app, server |
| Microsoft.SqlServer.Server | 1.0.0 | MIT | Microsoft.Data.SqlClient | Functions app, server |
| ModelContextProtocol.Core | 2.4.0 | Apache-2.0 | ModelContextProtocol | Functions app, server |
| OpenTelemetry | 1.15.3 | Apache-2.0 | OpenTelemetry.Extensions.Hosting | Server |
| OpenTelemetry | 1.18.0 | Apache-2.0 | Microsoft.Azure.Functions.Worker.OpenTelemetry | Functions app |
| OpenTelemetry.Api | 1.15.3 | Apache-2.0 | OpenTelemetry.Api.ProviderBuilderExtensions | Server |
| OpenTelemetry.Api | 1.18.0 | Apache-2.0 | Microsoft.Azure.Functions.Worker.OpenTelemetry | Functions app |
| OpenTelemetry.Api.ProviderBuilderExtensions | 1.15.3 | Apache-2.0 | OpenTelemetry | Server |
| OpenTelemetry.Api.ProviderBuilderExtensions | 1.18.0 | Apache-2.0 | OpenTelemetry | Functions app |
| OpenTelemetry.Extensions.Hosting | 1.15.3 | Apache-2.0 | Azure.Monitor.OpenTelemetry.AspNetCore | Server |
| OpenTelemetry.Extensions.Hosting | 1.18.0 | Apache-2.0 | Azure.Monitor.OpenTelemetry.Exporter | Functions app |
| OpenTelemetry.Instrumentation.AspNetCore | 1.15.2 | Apache-2.0 | Azure.Monitor.OpenTelemetry.AspNetCore | Server |
| OpenTelemetry.Instrumentation.Http | 1.15.1 | Apache-2.0 | Azure.Monitor.OpenTelemetry.AspNetCore | Server |
| OpenTelemetry.PersistentStorage.Abstractions | 1.0.3 | Apache-2.0 | OpenTelemetry.PersistentStorage.FileSystem | Server |
| OpenTelemetry.PersistentStorage.Abstractions | 1.1.1 | Apache-2.0 | OpenTelemetry.PersistentStorage.FileSystem | Functions app |
| OpenTelemetry.PersistentStorage.FileSystem | 1.0.3 | Apache-2.0 | Azure.Monitor.OpenTelemetry.Exporter | Server |
| OpenTelemetry.PersistentStorage.FileSystem | 1.1.1 | Apache-2.0 | Azure.Monitor.OpenTelemetry.Exporter | Functions app |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.13 | Apache-2.0 | Microsoft.Data.Sqlite | Server |
| SQLitePCLRaw.core | 2.1.13 | Apache-2.0 | Microsoft.Data.Sqlite | Server |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.13 | Apache-2.0 | SQLitePCLRaw.bundle_e_sqlite3 | Server |
| SQLitePCLRaw.provider.e_sqlite3 | 2.1.13 | Apache-2.0 | SQLitePCLRaw.bundle_e_sqlite3 | Server |
| System.ClientModel | 1.8.0 | MIT | Azure.Core | Library and test builds only |
| System.ClientModel | 1.9.0 | MIT | Azure.Core | Library and test builds only |
| System.ClientModel | 1.14.0 | MIT | Azure.Core | Server |
| System.ClientModel | 1.16.0 | MIT | Azure.Core | Functions app |
| System.Configuration.ConfigurationManager | 9.0.18 | MIT | Microsoft.Data.SqlClient | Functions app, server |
| System.Diagnostics.EventLog | 9.0.18 | MIT | System.Configuration.ConfigurationManager | Library and test builds only |
| System.Diagnostics.EventLog | 10.0.0 | MIT | Microsoft.Extensions.Logging.EventLog | Functions app |
| System.Diagnostics.EventLog | 10.0.14 | MIT | Microsoft.Extensions.Logging.EventLog | Tests only |
| System.IdentityModel.Tokens.Jwt | 8.16.0 | MIT | Microsoft.IdentityModel.Protocols.OpenIdConnect | Library and test builds only |
| System.IdentityModel.Tokens.Jwt | 8.23.0 | MIT | Microsoft.Identity.Web | Functions app, server |
| System.Memory.Data | 8.0.1 | MIT | Azure.Core | Library and test builds only |
| System.Memory.Data | 10.0.1 | MIT | Azure.Core | Library and test builds only |
| System.Memory.Data | 10.0.9 | MIT | Azure.Core | Server |
| System.Memory.Data | 10.0.10 | MIT | Azure.Core | Functions app |
| System.Security.Cryptography.Pkcs | 10.0.13 | MIT | Microsoft.Identity.Web.TokenCache | Library and test builds only |
| System.Security.Cryptography.ProtectedData | 4.5.0 | MIT (license URL to the dotnet repo; license file in the package) | Microsoft.Identity.Client.Extensions.Msal | Library and test builds only |
| System.Security.Cryptography.ProtectedData | 9.0.18 | MIT | Microsoft.Identity.Client.Extensions.Msal | Functions app, server |
| System.Security.Cryptography.Xml | 10.0.13 | MIT | Microsoft.AspNetCore.DataProtection | Functions app |
| System.Threading.RateLimiting | 9.0.18 | MIT | Microsoft.Data.SqlClient | Functions app |

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
and protocol handling use the engine's own code. The core does not reference the official MCP C# SDK: the library
sits behind `IMcpLibraryAdapter` with a pass-through stub. The Azure adapter references the SDK (ModelContextProtocol
and ModelContextProtocol.AspNetCore 2.4.0, Apache-2.0): its tests use the SDK client, and its host offers the SDK
server transport as an option behind that seam. Both are listed under ".NET: adapters/azure".

## Node: ui/harness only

The web app: React 19 and shadcn/ui (Radix primitives, Tailwind CSS 4, lucide icons), built by Vite (D-32 moved it
off Fluent UI on 2026-11-23). The shadcn/ui components are MIT source copied into `ui/harness/src/components/ui/`, not a
package. One source gives two builds: the local harness on fixture JSON, served by Vite on 127.0.0.1, and the web app,
which the product's host serves under `/app/`.
Installed with `npm install --before=2026-11-09 --ignore-scripts` in `ui/harness/` on 2026-11-23, so every package was
at least two weeks old that day; the lock file pins them, and CI installs with `npm ci --ignore-scripts`.
No Babel: esbuild compiles the TSX, so the React plugin for Vite and its Babel tree are not used.
The web app signs in with MSAL for the browser: @azure/msal-browser 5.29.0 and its exact dependency @azure/msal-common
16.16.1, both MIT. msal-react is not used. esbuild is also a direct devDependency, because `scripts/run-tests.mjs`
bundles the unit tests with it.
`node ui/harness/scripts/check-packages.mjs` checks this table against the lock file (the `ui` check of `tools/ci.mjs`).

Licenses outside MIT, Apache-2.0 and BSD-2-Clause or BSD-3-Clause, still to be confirmed by the product owner:
- **lucide-react 1.70.0, ISC.** The icons on the pages, in the page bundle. ISC is a short permissive license, close to
  MIT, but it is not on the house list.
- **lightningcss 1.33.0 and its platform binaries, MPL-2.0.** The CSS compiler inside Tailwind CSS 4. Build time only,
  never in the page bundle, used unchanged. MPL-2.0 is a file-level copyleft license: it would apply only to changes to
  lightningcss's own files, and there are none.
- **picocolors 1.1.1 and graceful-fs 4.2.11, ISC.** Terminal colors for postcss inside the Vite build, and file reads
  inside the Tailwind CSS build. Build time only; never in the page bundle.

| Package | Version | License | Direct or transitive | Use |
|---|---|---|---|---|
| @azure/msal-browser | 5.29.0 | MIT | Direct | Runtime dependency, web app only. Entra ID sign-in (authorization code flow with PKCE, full-page redirect) and access tokens for the product API |
| @azure/msal-common | 16.16.1 | MIT | Transitive (@azure/msal-browser) | Runtime dependency |
| @emnapi/core | 1.11.3 | MIT | Transitive (@napi-rs/wasm-runtime) | Build only |
| @emnapi/runtime | 1.11.3 | MIT | Transitive (@napi-rs/wasm-runtime) | Build only |
| @emnapi/wasi-threads | 1.2.3 | MIT | Transitive (@emnapi/core) | Build only |
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
| @floating-ui/dom | 1.8.0 | MIT | Transitive (@floating-ui/react-dom) | Runtime dependency |
| @floating-ui/react-dom | 2.1.9 | MIT | Transitive (@radix-ui/react-popper) | Runtime dependency |
| @floating-ui/utils | 0.2.12 | MIT | Transitive (@floating-ui/core) | Runtime dependency |
| @jridgewell/gen-mapping | 0.3.13 | MIT | Transitive (@jridgewell/remapping) | Build only |
| @jridgewell/remapping | 2.3.5 | MIT | Transitive (@tailwindcss/node) | Build only |
| @jridgewell/resolve-uri | 3.1.2 | MIT | Transitive (@jridgewell/trace-mapping) | Build only |
| @jridgewell/sourcemap-codec | 1.6.0 | MIT | Transitive (@jridgewell/gen-mapping) | Build only |
| @jridgewell/trace-mapping | 0.3.31 | MIT | Transitive (@jridgewell/gen-mapping) | Build only |
| @napi-rs/wasm-runtime | 1.2.2 | MIT | Transitive (@tailwindcss/oxide-wasm32-wasi) | Build only |
| @radix-ui/number | 1.1.3 | MIT | Transitive (@radix-ui/react-one-time-password-field) | Runtime dependency |
| @radix-ui/primitive | 1.2.0 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-accessible-icon | 1.2.0 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-accordion | 1.3.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-alert-dialog | 1.2.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-arrow | 1.1.17 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-aspect-ratio | 1.2.0 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-avatar | 1.3.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-checkbox | 1.4.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-collapsible | 1.2.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-collection | 1.2.0 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-compose-refs | 1.1.5 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-context | 1.3.0 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-context-menu | 2.4.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-dialog | 1.3.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-direction | 1.2.0 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-dismissable-layer | 1.2.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-dropdown-menu | 2.2.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-focus-guards | 1.2.0 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-focus-scope | 1.3.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-form | 0.3.0 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-hover-card | 1.2.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-id | 1.1.4 | MIT | Transitive (@radix-ui/react-accordion) | Runtime dependency |
| @radix-ui/react-label | 2.1.17 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-menu | 2.2.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-menubar | 1.2.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-navigation-menu | 1.4.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-one-time-password-field | 0.2.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-password-toggle-field | 0.2.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-popover | 1.3.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-popper | 1.4.0 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-portal | 1.2.0 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-presence | 1.1.11 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-primitive | 2.2.0 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-progress | 1.2.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-radio-group | 1.5.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-roving-focus | 1.2.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-scroll-area | 1.4.0 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-select | 2.4.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-separator | 1.2.0 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-slider | 1.6.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-slot | 1.5.0 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-switch | 1.4.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-tabs | 1.2.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-toast | 1.3.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-toggle | 1.2.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-toggle-group | 1.2.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-toolbar | 1.2.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-tooltip | 1.4.1 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-use-callback-ref | 1.1.5 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-use-controllable-state | 1.3.0 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-use-effect-event | 0.0.5 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-use-escape-keydown | 1.1.6 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-use-is-hydrated | 0.1.3 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-use-layout-effect | 1.1.4 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-use-previous | 1.1.4 | MIT | Transitive (@radix-ui/react-navigation-menu) | Runtime dependency |
| @radix-ui/react-use-rect | 1.1.4 | MIT | Transitive (@radix-ui/react-popper) | Runtime dependency |
| @radix-ui/react-use-size | 1.1.5 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/react-visually-hidden | 1.2.13 | MIT | Transitive (radix-ui) | Runtime dependency |
| @radix-ui/rect | 1.1.3 | MIT | Transitive (@radix-ui/react-popper) | Runtime dependency |
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
| @tailwindcss/node | 4.3.5 | MIT | Transitive (@tailwindcss/vite) | Build only |
| @tailwindcss/oxide | 4.3.5 | MIT | Transitive (@tailwindcss/vite) | Build only |
| @tailwindcss/oxide-android-arm64 | 4.3.5 | MIT | Transitive (@tailwindcss/oxide) | Build only. Optional platform binary for another system; not installed here |
| @tailwindcss/oxide-darwin-arm64 | 4.3.5 | MIT | Transitive (@tailwindcss/oxide) | Build only. Optional platform binary for another system; not installed here |
| @tailwindcss/oxide-darwin-x64 | 4.3.5 | MIT | Transitive (@tailwindcss/oxide) | Build only. Optional platform binary for another system; not installed here |
| @tailwindcss/oxide-freebsd-x64 | 4.3.5 | MIT | Transitive (@tailwindcss/oxide) | Build only. Optional platform binary for another system; not installed here |
| @tailwindcss/oxide-linux-arm-gnueabihf | 4.3.5 | MIT | Transitive (@tailwindcss/oxide) | Build only. Optional platform binary for another system; not installed here |
| @tailwindcss/oxide-linux-arm64-gnu | 4.3.5 | MIT | Transitive (@tailwindcss/oxide) | Build only. Optional platform binary for another system; not installed here |
| @tailwindcss/oxide-linux-arm64-musl | 4.3.5 | MIT | Transitive (@tailwindcss/oxide) | Build only. Optional platform binary for another system; not installed here |
| @tailwindcss/oxide-linux-x64-gnu | 4.3.5 | MIT | Transitive (@tailwindcss/oxide) | Build only. Optional platform binary for another system; not installed here |
| @tailwindcss/oxide-linux-x64-musl | 4.3.5 | MIT | Transitive (@tailwindcss/oxide) | Build only. Optional platform binary for another system; not installed here |
| @tailwindcss/oxide-wasm32-wasi | 4.3.5 | MIT | Transitive (@tailwindcss/oxide) | Build only |
| @tailwindcss/oxide-win32-arm64-msvc | 4.3.5 | MIT | Transitive (@tailwindcss/oxide) | Build only. Optional platform binary for another system; not installed here |
| @tailwindcss/oxide-win32-x64-msvc | 4.3.5 | MIT | Transitive (@tailwindcss/oxide) | Build only. Optional platform binary, the one installed on Windows x64 |
| @tailwindcss/vite | 4.3.5 | MIT | Direct | Build only. Runs Tailwind CSS inside the Vite build |
| @tybys/wasm-util | 0.10.3 | MIT | Transitive (@napi-rs/wasm-runtime) | Build only |
| @types/estree | 1.0.10 | MIT | Transitive (rollup) | Types only, never in the bundle |
| @types/react | 19.3.0 | MIT | Direct | Types only, never in the bundle. Types for the typecheck |
| @types/react-dom | 19.3.0 | MIT | Direct | Types only, never in the bundle. Types for the typecheck |
| aria-hidden | 1.2.6 | MIT | Transitive (@radix-ui/react-dialog) | Runtime dependency |
| class-variance-authority | 0.7.1 | Apache-2.0 | Direct | Runtime dependency. Component variants of the shadcn/ui components |
| clsx | 2.1.1 | MIT | Direct | Runtime dependency. Class name joining (the cn helper) |
| csstype | 3.2.3 | MIT | Transitive (@types/react) | Build only |
| detect-libc | 2.1.2 | Apache-2.0 | Transitive (lightningcss) | Build only |
| detect-node-es | 1.1.0 | MIT | Transitive (use-sidecar) | Runtime dependency |
| enhanced-resolve | 5.26.1 | MIT | Transitive (@tailwindcss/node) | Build only |
| esbuild | 0.28.2 | MIT | Direct | Build only. Bundles the unit tests (scripts/run-tests.mjs) |
| fdir | 6.5.0 | MIT | Transitive (vite) | Build only |
| fsevents | 2.3.3 | MIT | Transitive (vite) | Build only. Optional platform binary for another system; not installed here |
| get-nonce | 1.0.1 | MIT | Transitive (react-style-singleton) | Runtime dependency |
| graceful-fs | 4.2.11 | ISC | Transitive (enhanced-resolve) | Build only |
| jiti | 2.7.0 | MIT | Transitive (vite) | Build only |
| lightningcss | 1.33.0 | MPL-2.0 | Transitive (vite) | Build only |
| lightningcss-android-arm64 | 1.33.0 | MPL-2.0 | Transitive (lightningcss) | Build only. Optional platform binary for another system; not installed here |
| lightningcss-darwin-arm64 | 1.33.0 | MPL-2.0 | Transitive (lightningcss) | Build only. Optional platform binary for another system; not installed here |
| lightningcss-darwin-x64 | 1.33.0 | MPL-2.0 | Transitive (lightningcss) | Build only. Optional platform binary for another system; not installed here |
| lightningcss-freebsd-x64 | 1.33.0 | MPL-2.0 | Transitive (lightningcss) | Build only. Optional platform binary for another system; not installed here |
| lightningcss-linux-arm-gnueabihf | 1.33.0 | MPL-2.0 | Transitive (lightningcss) | Build only. Optional platform binary for another system; not installed here |
| lightningcss-linux-arm64-gnu | 1.33.0 | MPL-2.0 | Transitive (lightningcss) | Build only. Optional platform binary for another system; not installed here |
| lightningcss-linux-arm64-musl | 1.33.0 | MPL-2.0 | Transitive (lightningcss) | Build only. Optional platform binary for another system; not installed here |
| lightningcss-linux-x64-gnu | 1.33.0 | MPL-2.0 | Transitive (lightningcss) | Build only. Optional platform binary for another system; not installed here |
| lightningcss-linux-x64-musl | 1.33.0 | MPL-2.0 | Transitive (lightningcss) | Build only. Optional platform binary for another system; not installed here |
| lightningcss-win32-arm64-msvc | 1.33.0 | MPL-2.0 | Transitive (lightningcss) | Build only. Optional platform binary for another system; not installed here |
| lightningcss-win32-x64-msvc | 1.33.0 | MPL-2.0 | Transitive (lightningcss) | Build only. Optional platform binary, the one installed on Windows x64 |
| lucide-react | 1.70.0 | ISC | Direct | Runtime dependency. The icons on the pages. ISC, a listed exception |
| magic-string | 1.4.5 | MIT | Transitive (@tailwindcss/node) | Build only |
| nanoid | 3.3.20 | MIT | Transitive (postcss) | Build only |
| picocolors | 1.1.1 | ISC | Transitive (postcss) | Build only |
| picomatch | 4.0.7 | MIT | Transitive (vite) | Build only |
| postcss | 8.5.32 | MIT | Transitive (vite) | Build only |
| radix-ui | 1.8.1 | MIT | Direct | Runtime dependency. The accessible primitives under the shadcn/ui components (tooltip, tabs, dialog, popover, toast, avatar) |
| react | 19.3.1 | MIT | Direct | Runtime dependency. UI runtime |
| react-dom | 19.3.1 | MIT | Direct | Runtime dependency. UI runtime in the browser |
| react-remove-scroll | 2.7.2 | MIT | Transitive (@radix-ui/react-dialog) | Runtime dependency |
| react-remove-scroll-bar | 2.3.8 | MIT | Transitive (react-remove-scroll) | Runtime dependency |
| react-style-singleton | 2.2.3 | MIT | Transitive (react-remove-scroll) | Runtime dependency |
| rollup | 4.68.0 | MIT | Transitive (vite) | Build only |
| scheduler | 0.28.0 | MIT | Transitive (react-dom) | Runtime dependency |
| source-map-js | 1.2.2 | BSD-3-Clause | Transitive (@tailwindcss/node) | Build only |
| tailwind-merge | 3.7.0 | MIT | Direct | Runtime dependency. Resolves conflicting Tailwind classes (the cn helper) |
| tailwindcss | 4.3.5 | MIT | Direct | Build only. Builds the static style sheet from src/styles/globals.css |
| tapable | 2.3.3 | MIT | Transitive (enhanced-resolve) | Build only |
| tinyglobby | 0.2.17 | MIT | Transitive (vite) | Build only |
| tslib | 2.8.1 | 0BSD | Transitive (@emnapi/core) | Build only |
| tslib | 2.9.1 | 0BSD | Transitive (@emnapi/core) | Runtime dependency |
| tw-animate-css | 1.4.0 | MIT | Direct | Build only. Animation utilities, compiled into the static style sheet |
| typescript | 5.9.3 | Apache-2.0 | Direct | Build only. Typecheck (tsc --noEmit). The JavaScript compiler, no native binary |
| use-callback-ref | 1.3.3 | MIT | Transitive (react-remove-scroll) | Runtime dependency |
| use-sidecar | 1.1.3 | MIT | Transitive (react-remove-scroll) | Runtime dependency |
| vite | 7.3.7 | MIT | Direct | Build only. Local build, and the preview server on 127.0.0.1 for the screenshots |

## Node: tests/e2e only

The end-to-end suite (`tests/e2e/`): one Playwright Test spec per demo scenario, run against a running web app
(docs/testing.md, "The end-to-end tests"). Test time only: nothing of it is in the web app, the server or any build the
product ships. Playwright drives the Chrome already installed on the machine (channel `chrome`), so no browser is
downloaded: install with `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 npm ci --ignore-scripts` in `tests/e2e/`.
Installed with `npm install --before=2026-11-11 --ignore-scripts` on 2026-11-25, so every package was at least two
weeks old that day (1.65.0 was published on 2026-11-05); the lock file pins them with sha512 hashes.
`node ui/harness/scripts/check-packages.mjs --package tests/e2e` checks this table against the lock file (the `e2e`
check of `tools/ci.mjs`).

| Package | Version | License | Direct or transitive | Use |
|---|---|---|---|---|
| @playwright/test | 1.65.0 | Apache-2.0 | Direct | Test only. The test runner: specs, projects, fixtures, traces, the HTML report |
| playwright | 1.65.0 | Apache-2.0 | Transitive (@playwright/test) | Test only. The browser automation library behind the runner |
| playwright-core | 1.65.0 | Apache-2.0 | Transitive (playwright) | Test only. The protocol client that drives the installed Chrome; no browser is downloaded |
