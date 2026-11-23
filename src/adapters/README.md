# adapters

The platform adapters around the engine. The engine is a portable library: its runtime talks to data only through two
storage ports, and the MCP server core already holds every tool and an in-process backend. An adapter adds the hosts
and stores a platform needs and changes neither. Decision D-26 answered D-22: the first release is designed to run on
Azure, in the client's own subscription.

| Folder | What | Status |
|---|---|---|
| `azure/` | The Azure adapter: a host library with the MCP endpoint, a REST API, the record and approval outcome endpoints and Microsoft Entra ID sign-in, the product's web app under `/app` (the four read-only pages), the approval runner's routes over an approval outbox, and the directory reads on behalf of the signed-in admin; the approval runner as a Power Automate template; a local server with a development identity for local runs; an Azure Functions wrapper; SQLite and Azure SQL stores; the Azure SQL scripts; an Azure Developer CLI template with Bicep; and two test projects. Its own solution, `azure/Product.Azure.sln` | Built and tested; early work, not a release. Nothing is deployed, the template has not been provisioned, the approval runner's flows have not been imported into a tenant, no sign-in or directory read has run against a real Microsoft Entra tenant, and the Azure SQL store has not run against a real database |

The Azure adapter runs on one machine over a SQLite file with fictional demo data and no Azure subscription or sign-in:
the MCP endpoint, the REST API, the web app, the approval loop with a stand-in for the approval runner, and the
directory reads over recorded, fictional responses.
[`azure/README.md`](azure/README.md) has the design mapping and the steps to run it locally, and
[`azure/STATUS.md`](azure/STATUS.md) lists what works today, the test counts and what is designed but not built yet.
[`azure/ENGINE-CHANGES-NEEDED.md`](azure/ENGINE-CHANGES-NEEDED.md) lists the changes to the engine and the MCP core
that the adapter needs, each with the workaround it uses until then.

A `dataverse/` adapter stays possible later, because the engine is a portable library.
