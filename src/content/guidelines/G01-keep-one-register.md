# G1. Keep one register of business applications

- **Number:** G1
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 5 (One register of business applications)
- **Cited in chapters:** 1, 3, 5, 17, 19, 22
- **Processes:** P01, P06

**Rule.** Keep one register that lists every business application your organization runs, whatever it is built on. An inventory of technical resources feeds the register. It is not the register.

**Why it matters.** Microsoft's inventory lists apps, flows, agents and sites. It does not say which of them form one business application, or who answers for it. Without a register, owners, risk and retirement decisions have no shared home.

**How the product is designed to support it.** It is designed to hold one record per business application, link each Microsoft resource by ID instead of copying it, and run an on-demand gap check that asks Microsoft's inventory, as the signed-in admin, what is running but not registered.

## Microsoft anchors

- [Power Platform inventory](https://learn.microsoft.com/power-platform/admin/power-platform-inventory), checked 2026-11-22. It gives tenant admins "a unified view of all agents, apps, flows, and sites". It includes agents made in Copilot Studio and in the Agent Builder of Microsoft Copilot (formerly Microsoft 365 Copilot). Its queries need delegated user sign-in: service principals and managed identities get HTTP 403. Viewing it needs a supported Microsoft Entra role, such as Global administrator, Power Platform administrator, Dynamics 365 administrator or Global reader.
- [Microsoft Copilot requirements](https://learn.microsoft.com/copilot/plan/microsoft-copilot-requirements), checked 2026-11-22: "Microsoft 365 Copilot is now named Microsoft Copilot".
- [Architecture and governance for AI agents](https://learn.microsoft.com/azure/cloud-adoption-framework/ai-agents/architecture-governance) (Cloud Adoption Framework), checked 2026-11-22: "Every agent should be discoverable through an approved inventory process." Governance teams should add inventory data from other platforms so the inventory stays complete.

## Maps to

NIST CSF 2.0 ID.AM (Asset Management).

## Working shape

- **When:** at intake for every new app, agent, AI-built app or SaaS tool, and each time an admin runs a gap check.
- **Who:** the platform team keeps the register; business owners claim their apps.
- **Steps:**
  1. Create the register entry the day an app is proposed, before build starts.
  2. Link each Microsoft resource that belongs to it (app, flow, agent, site, environment) by ID and URL. Do not copy inventory data.
  3. At each periodic review, have an admin compare the register with Microsoft's inventory, live and on demand. Send each unregistered resource to a likely owner to claim or retire.
- **Done when:** every resource in the inventory belongs to a registered application or is marked for retirement.
- **Evidence:** the register entry with its resource links, and the date and claim list of the last gap check.
