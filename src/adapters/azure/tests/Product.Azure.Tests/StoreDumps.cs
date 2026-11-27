using System;
using System.Collections.Generic;
using System.Linq;
using Product.Azure.Stores;
using Product.Engine.Runtime.Ports;

namespace Product.Azure.Tests;

// A refused call writes nothing but the product event that records it (document 25, B25-13: one audit entry, operation
// call-refused, in a transaction of its own). A test that proves "nothing was written" compares the store without its
// audit entries and checks the refusal entries on their own.
internal static class StoreDumps
{
    public static string WithoutAudit(ProductTestHost host)
    {
        return WithoutAudit(((RowStore)host.Get<IProductStore>()).Dump());
    }

    public static string WithoutAudit(string dump)
    {
        return string.Join("\n", dump.Split('\n').Where(line => !line.StartsWith("audit-entry ", StringComparison.Ordinal)));
    }

    // The call-refused entries in the store, oldest first.
    public static List<StoredRow> Refusals(ProductTestHost host)
    {
        return host.Get<IRecordStore>().Query(RowQuery.For("audit-entry").Where("operation", "call-refused"))
            .OrderBy(r => r.Text("occurredOn") ?? string.Empty, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
    }
}
