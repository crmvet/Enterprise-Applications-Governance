# Third-party packages

Every package below comes from a public feed: nuget.org for .NET and npmjs.com for Node.
Every version is exact and pinned. Licenses are MIT, Apache 2.0 or BSD (0BSD counts as BSD here), with these
exceptions:
- picocolors (ISC), a build-only package of the page harness, still open (see "Node: ui/harness only");
- two transitive Microsoft packages of the Azure adapter under the Microsoft Software License Terms,
  Microsoft.Data.SqlClient.SNI.runtime and Microsoft.Identity.Client.NativeInterop. They are restored from NuGet
  under Microsoft's terms and are not part of this repository's MIT-licensed source (see ".NET: adapters/azure").

Licenses were read from each package's own metadata on 2026-11-22, and for the Azure adapter on 2026-11-23.

## Notices in shipped builds

This repository holds no third-party code. Packages are restored from the feeds, and build output (`bin/`, `obj/`,
`node_modules/`, `ui/harness/dist/`) is never committed. MIT, BSD, ISC and Apache 2.0 all ask that the license text
goes with every copy of the package. So any build that is handed to others must carry the license text, and any
notice file, of every package inside it. Four builds would carry packages:

- the Dataverse plug-in package: System.Text.Json and the System packages it needs on net48 (all MIT);
- a built page bundle (the harness or the web app): React, Fluent UI and their runtime packages (MIT), in the web app also
  MSAL for the browser (@azure/msal-browser and @azure/msal-common, MIT), plus @swc/helpers (Apache 2.0) and tslib (0BSD);
- the Azure adapter's Functions deployment package: the packages marked "Functions app" in the column "In" of both
  tables under ".NET: adapters/azure", including the two Microsoft packages listed there under their own terms, and,
  because the package carries the built web app under `wwwroot/app`, the packages of the web app's page bundle above;
- the Azure adapter's server container image: the packages marked "Server" there, including the same two, on top of
  the .NET base image, which carries its own notices.

Build-only and test-only packages (xunit, Vite, esbuild, Rollup, postcss, TypeScript) are never shipped.

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

The four pages: React and Fluent UI v9, built by Vite. One source gives two builds: the local harness on fixture JSON,
served by Vite on 127.0.0.1, and the web app, which the product's host serves under `/app/`.
Installed with `npm ci --ignore-scripts` in `ui/harness/`. Every package was published on or before 2026-11-08
(the install used `--before=2026-11-08`), so each one was at least two weeks old on 2026-11-22.
No Babel: esbuild compiles the TSX, so the React plugin for Vite and its Babel tree are not used.
The web app signs in with MSAL for the browser: @azure/msal-browser 5.29.0 and its exact dependency @azure/msal-common
16.16.1, both MIT and both published on 2026-11-06, added on 2026-11-23 with the same `--before=2026-11-08` (17 days
old that day). msal-react is not used. esbuild is also a direct devDependency, because `scripts/run-tests.mjs` bundles
the unit tests with it.
`node ui/harness/scripts/check-packages.mjs` checks this table against the lock file (the `ui` check of `tools/ci.mjs`).

Licenses outside MIT, Apache-2.0 and BSD-2-Clause or BSD-3-Clause, still to be confirmed by the product owner:
- **picocolors 1.1.1, ISC.** postcss needs it for terminal colors, and Vite needs postcss. Build time only; it is never
  in the page bundle. ISC is a short permissive license, close to MIT, but it is not on the house list. No Vite
  release drops postcss. If the exception is refused, the harness can move from Vite to esbuild alone (esbuild is MIT).
- **tslib 2.9.1, 0BSD (BSD Zero Clause).** Runtime helpers used by Fluent's styling and icon packages. Counted as BSD here.

| Package | Version | License | Direct or transitive | Use |
|---|---|---|---|---|
| @azure/msal-browser | 5.29.0 | MIT | Direct | Runtime dependency, web app only. Entra ID sign-in (authorization code flow with PKCE, full-page redirect) and access tokens for the product API |
| @azure/msal-common | 16.16.1 | MIT | Transitive (@azure/msal-browser) | Runtime dependency, web app only |
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
| esbuild | 0.28.2 | MIT | Direct, and transitive (vite) | Build only. Vite's compiler, and the bundler of the unit tests (`scripts/run-tests.mjs`) |
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
