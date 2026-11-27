using Product.Engine.Core.Canonical;

namespace Product.Mcp.Core.Tests;

// The data world of the MCP tests (data taxonomy slice 3; docs/architecture/data-taxonomy.md): a small data domain
// tree in version 1, and the locations, mappings, the use and the domain owners the data tools read. Every name is
// fictional and every ID a patterned placeholder.
//
// Tree: Finance (internal; data owner the deputy person, with the architect as deputy) > Expense claims (confidential)
// and Payroll (restricted, AI use never; data owner the reader person) > Bank details. Operations (internal, no owner) >
// Travel bookings.
// Locations: Fixture finance production (Dataverse, production, the production target), Fixture payroll host (IBM i,
// production; technical owner the owner person) with its library PAYLIB, and Fixture finance test (Dataverse, test).
// Mappings: Expense claims in production (system of record) and in test (a masked copy); Payroll and Bank details in
// PAYLIB. Use: the expense app reads Expense claims in production, with no AI (a decision is needed and none is open).
internal static class DataWorld
{
    public const string FinanceProd = "00000000-0000-4000-8000-000000000581";
    public const string PayrollHost = "00000000-0000-4000-8000-000000000582";
    public const string PayrollLibrary = "00000000-0000-4000-8000-000000000583";
    public const string FinanceTest = "00000000-0000-4000-8000-000000000584";

    public const string ExpensesInProd = "00000000-0000-4000-8000-000000000591";
    public const string PayrollInLibrary = "00000000-0000-4000-8000-000000000592";
    public const string BankDetailsInLibrary = "00000000-0000-4000-8000-000000000593";
    public const string MaskedExpensesInTest = "00000000-0000-4000-8000-000000000594";

    public const string ExpenseReadsClaims = "00000000-0000-4000-8000-0000000005a1";

    public const string FinanceOwner = "00000000-0000-4000-8000-000000000571";
    public const string FinanceDeputy = "00000000-0000-4000-8000-000000000572";
    public const string PayrollOwner = "00000000-0000-4000-8000-000000000573";
    public const string HostTechnicalOwner = "00000000-0000-4000-8000-000000000574";

    public const string ConfigurationOperations = "["
        + "{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"finance\",\"values\":{\"label\":\"Finance\",\"levelKey\":\"domain\",\"classificationKey\":\"internal\",\"examples\":\"Money in and out: expenses, budgets and payroll.\",\"sortOrder\":1}},"
        + "{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"expenses\",\"values\":{\"label\":\"Expense claims\",\"parentKey\":\"finance\",\"levelKey\":\"subdomain\",\"classificationKey\":\"confidential\",\"examples\":\"Claims, receipts and approvals.\",\"sortOrder\":2}},"
        + "{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"payroll\",\"values\":{\"label\":\"Payroll\",\"parentKey\":\"finance\",\"levelKey\":\"subdomain\",\"classificationKey\":\"restricted\",\"aiUseRule\":\"never\",\"examples\":\"Salary, deductions and tax codes.\",\"sortOrder\":3}},"
        + "{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"bank-details\",\"values\":{\"label\":\"Bank details\",\"parentKey\":\"payroll\",\"levelKey\":\"data-set\",\"examples\":\"Bank accounts for pay.\",\"sortOrder\":4}},"
        + "{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"operations\",\"values\":{\"label\":\"Operations\",\"levelKey\":\"domain\",\"classificationKey\":\"internal\",\"examples\":\"Day to day work: travel and facilities.\",\"sortOrder\":5}},"
        + "{\"op\":\"upsert\",\"entity\":\"data-domain\",\"key\":\"travel-bookings\",\"values\":{\"label\":\"Travel bookings\",\"parentKey\":\"operations\",\"levelKey\":\"subdomain\",\"examples\":\"Trips, flights and hotels.\",\"sortOrder\":6}}"
        + "]";

    public static void Seed(McpHarness h)
    {
        h.Store.Seed("owner-assignment", "{\"subjectKind\":\"data-domain\",\"subjectDataDomain\":\"finance\",\"ownerRole\":\"data-owner\",\"principal\":\"" + McpHarness.Deputy + "\",\"isDeputy\":false,\"startOn\":\"2026-01-01\"}", id: FinanceOwner);
        h.Store.Seed("owner-assignment", "{\"subjectKind\":\"data-domain\",\"subjectDataDomain\":\"finance\",\"ownerRole\":\"data-owner\",\"principal\":\"" + McpHarness.Architect + "\",\"isDeputy\":true,\"deputyFor\":\"" + FinanceOwner + "\",\"startOn\":\"2026-01-01\"}", id: FinanceDeputy);
        h.Store.Seed("owner-assignment", "{\"subjectKind\":\"data-domain\",\"subjectDataDomain\":\"payroll\",\"ownerRole\":\"data-owner\",\"principal\":\"" + McpHarness.Reader + "\",\"isDeputy\":false,\"startOn\":\"2026-01-01\"}", id: PayrollOwner);
        h.Store.Seed("owner-assignment", "{\"subjectKind\":\"data-location\",\"subjectDataLocation\":\"" + PayrollHost + "\",\"ownerRole\":\"technical-owner\",\"principal\":\"" + McpHarness.Owner + "\",\"isDeputy\":false,\"startOn\":\"2026-01-01\"}", id: HostTechnicalOwner);

        h.Store.Seed("data-location", "{\"dataPlatform\":\"dataverse\",\"targetStage\":\"production\",\"deploymentTarget\":\"" + McpHarness.Production + "\",\"linkKind\":\"dataverse-environment\",\"externalId\":\"fixture-env-prod\","
            + "\"url\":\"https://fixture-finance.crm.example.org\",\"labelAsSeen\":\"Fixture finance production\",\"lastSeenOn\":\"2026-11-01T00:00:00Z\",\"status\":\"data-location-registered\",\"statusMeaning\":\"registered\"}", id: FinanceProd);
        h.Store.Seed("data-location", "{\"dataPlatform\":\"ibm-i\",\"targetStage\":\"production\",\"linkKind\":\"external-data-system\",\"externalId\":\"payroll-host-01\",\"labelAsSeen\":\"Fixture payroll host\","
            + "\"lastSeenOn\":\"2026-11-01T00:00:00Z\",\"status\":\"data-location-registered\",\"statusMeaning\":\"registered\"}", id: PayrollHost);
        h.Store.Seed("data-location", "{\"dataPlatform\":\"ibm-i\",\"parentLocation\":\"" + PayrollHost + "\",\"linkKind\":\"external-data-system\",\"externalId\":\"PAYLIB\",\"labelAsSeen\":\"PAYLIB\","
            + "\"lastSeenOn\":\"2026-11-01T00:00:00Z\",\"status\":\"data-location-registered\",\"statusMeaning\":\"registered\"}", id: PayrollLibrary);
        h.Store.Seed("data-location", "{\"dataPlatform\":\"dataverse\",\"targetStage\":\"test\",\"linkKind\":\"dataverse-environment\",\"externalId\":\"fixture-env-test\",\"labelAsSeen\":\"Fixture finance test\","
            + "\"lastSeenOn\":\"2026-11-01T00:00:00Z\",\"status\":\"data-location-registered\",\"statusMeaning\":\"registered\"}", id: FinanceTest);

        h.Store.Seed("data-mapping", "{\"dataDomain\":\"expenses\",\"dataLocation\":\"" + FinanceProd + "\",\"containers\":[\"fx_expenseclaim\"],\"dataOrigin\":\"system-of-record\",\"status\":\"data-mapping-registered\",\"statusMeaning\":\"registered\",\"startOn\":\"2026-01-01\"}", id: ExpensesInProd);
        h.Store.Seed("data-mapping", "{\"dataDomain\":\"payroll\",\"dataLocation\":\"" + PayrollLibrary + "\",\"containers\":[\"PAYMAST\"],\"dataOrigin\":\"system-of-record\",\"status\":\"data-mapping-registered\",\"statusMeaning\":\"registered\",\"startOn\":\"2026-01-01\"}", id: PayrollInLibrary);
        h.Store.Seed("data-mapping", "{\"dataDomain\":\"bank-details\",\"dataLocation\":\"" + PayrollLibrary + "\",\"containers\":[\"PAYBANK\"],\"dataOrigin\":\"system-of-record\",\"status\":\"data-mapping-registered\",\"statusMeaning\":\"registered\",\"startOn\":\"2026-01-01\"}", id: BankDetailsInLibrary);
        h.Store.Seed("data-mapping", "{\"dataDomain\":\"expenses\",\"dataLocation\":\"" + FinanceTest + "\",\"dataOrigin\":\"masked-copy\",\"status\":\"data-mapping-registered\",\"statusMeaning\":\"registered\",\"startOn\":\"2026-02-01\"}", id: MaskedExpensesInTest);

        h.Store.Seed("data-use", "{\"recordNumber\":\"DU-0001\",\"businessApplication\":\"" + McpHarness.Expense + "\",\"dataDomain\":\"expenses\",\"dataLocation\":\"" + FinanceProd + "\",\"access\":\"read\","
            + "\"purpose\":\"Shows each claim to its approver.\",\"reachesAi\":false,\"isDeidentified\":false,\"status\":\"data-use-registered\",\"statusMeaning\":\"registered\",\"startOn\":\"2026-01-05\"}", id: ExpenseReadsClaims);
    }

    // The arguments of a data use the owner declares for the expense app.
    public static string Use(string domainKey, string access = "read", bool reachesAi = false, string? extra = null)
    {
        return "{\"applicationId\":\"" + McpHarness.Expense + "\",\"domainKey\":\"" + domainKey + "\",\"access\":\"" + access + "\",\"purpose\":\"Fixture purpose for " + domainKey + ".\",\"reachesAi\":"
            + (reachesAi ? "true" : "false") + (extra == null ? string.Empty : "," + extra) + "}";
    }

    public static JsonTreeNode Data(JsonTreeNode envelope)
    {
        return McpHarness.Data(envelope);
    }
}
