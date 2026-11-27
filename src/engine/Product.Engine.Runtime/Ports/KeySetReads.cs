using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Core.Canonical;

namespace Product.Engine.Runtime.Ports;

// Reads many rows in one store call (document 25, section 3.3: "an adapter turns it into one indexed select ... and may
// batch several"; B25-23). A store that implements it answers a read of many keys in one round trip, which matters on a
// database across a network: a page that shows 2,000 applications reads their owners once, not 2,000 times. Every store
// of the product implements it (InMemoryStore, the SQLite and Azure SQL stores, and their transactions); KeySetReads
// below falls back to one read per key on any other reader, with the same answer.
public interface IKeySetReader
{
    // The rows of the entity with these IDs, by ID (ordinal). An ID with no row is left out, and an ID given twice
    // returns its row once.
    IReadOnlyList<StoredRow> GetMany(string entity, IReadOnlyCollection<string> ids);

    // The rows of the entity whose column holds one of these values as a JSON string (ordinal), by ID (ordinal): the rows
    // Query(RowQuery.For(entity).Where(column, value)) returns for each value, together, each once.
    IReadOnlyList<StoredRow> QueryIn(string entity, string column, IReadOnlyCollection<string> values);
}

// The key set reads on any reader: one store call on an IKeySetReader, else one read per key.
public static class KeySetReads
{
    public static IReadOnlyList<StoredRow> GetMany(this IRecordReader reader, string entity, IEnumerable<string?> ids)
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        List<string> keys = Distinct(ids);
        if (keys.Count == 0)
        {
            return Array.Empty<StoredRow>();
        }

        if (reader is IKeySetReader batch)
        {
            return batch.GetMany(entity, keys);
        }

        return keys.Select(id => reader.Get(entity, id)).Where(r => r != null).Select(r => r!).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<StoredRow> QueryIn(this IRecordReader reader, string entity, string column, IEnumerable<string?> values)
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        List<string> keys = Distinct(values);
        if (keys.Count == 0)
        {
            return Array.Empty<StoredRow>();
        }

        if (reader is IKeySetReader batch)
        {
            return batch.QueryIn(entity, column, keys);
        }

        var rows = new SortedDictionary<string, StoredRow>(StringComparer.Ordinal);
        foreach (string value in keys)
        {
            foreach (StoredRow row in reader.Query(RowQuery.For(entity).Where(column, value)))
            {
                rows[row.Id] = row;
            }
        }

        return rows.Values.ToList();
    }

    // True when the row's column is a JSON string in the set: what a store's QueryIn keeps, checked exactly.
    public static bool HoldsOneOf(StoredRow row, string column, ISet<string> values)
    {
        if (row == null)
        {
            throw new ArgumentNullException(nameof(row));
        }

        JsonTreeNode? value = row.Column(column);
        return value != null && value.Kind == JsonTreeKind.String && values.Contains(value.GetString());
    }

    // The values once each, in the order given, without nulls.
    public static List<string> Distinct(IEnumerable<string?>? values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (string? value in values ?? Enumerable.Empty<string?>())
        {
            if (value != null && seen.Add(value))
            {
                list.Add(value);
            }
        }

        return list;
    }
}
