using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Product.Engine.Runtime.Ports;

namespace Product.Engine.Runtime.Facts;

// One typed query of a fact plan (document 25, section 3.3): a named fact and how to read it, by row ID, by key or by a
// query. The name says what the operation needs it for, such as "before" or "records".
public sealed class FactQuery
{
    private FactQuery(string name, string entity, string? id, string? key, RowQuery? query)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("A fact has a name.", nameof(name));
        }

        Name = name;
        Entity = entity;
        Id = id;
        Key = key;
        Query = query;
    }

    public string Name { get; }

    public string Entity { get; }

    public string? Id { get; }

    public string? Key { get; }

    public RowQuery? Query { get; }

    public static FactQuery ById(string name, string entity, string id)
    {
        return new FactQuery(name, entity, id ?? throw new ArgumentNullException(nameof(id)), null, null);
    }

    public static FactQuery ByKey(string name, string entity, string key)
    {
        return new FactQuery(name, entity, null, key ?? throw new ArgumentNullException(nameof(key)), null);
    }

    public static FactQuery Where(string name, RowQuery query)
    {
        return new FactQuery(name, (query ?? throw new ArgumentNullException(nameof(query))).Entity, null, null, query);
    }

    public override string ToString()
    {
        return Name + ": " + (Id != null ? Entity + " " + Id : Key != null ? Entity + " key " + Key : Query!.ToString());
    }
}

// The facts one round needs. A planner returns plans round by round, because a later query can depend on an earlier
// answer (the subject of a decision, then its application, then the application's portfolio).
public sealed class FactPlan
{
    private FactPlan(IList<FactQuery> queries)
    {
        Queries = new ReadOnlyCollection<FactQuery>(queries);
    }

    public IReadOnlyList<FactQuery> Queries { get; }

    public bool IsEmpty => Queries.Count == 0;

    public static FactPlan Of(params FactQuery?[] queries)
    {
        return new FactPlan(queries.Where(q => q != null).Select(q => q!).ToList());
    }

    public static FactPlan Of(IEnumerable<FactQuery> queries)
    {
        return new FactPlan(queries.ToList());
    }
}

// The fetched facts, by name. Fetching a name twice keeps the first answer.
public sealed class FactSet
{
    private readonly Dictionary<string, IReadOnlyList<StoredRow>> _facts = new(StringComparer.Ordinal);
    private readonly List<FactQuery> _queries = new();

    // How many rounds were fetched (round trips, batched by the adapter). Document 25 expects one to three.
    public int Rounds { get; private set; }

    // Every query run, in order. For tests and diagnostics.
    public IReadOnlyList<FactQuery> Queries => _queries;

    public bool Has(string name)
    {
        return _facts.ContainsKey(name);
    }

    public StoredRow? One(string name)
    {
        return _facts.TryGetValue(name, out IReadOnlyList<StoredRow>? rows) && rows.Count > 0 ? rows[0] : null;
    }

    public IReadOnlyList<StoredRow> All(string name)
    {
        return _facts.TryGetValue(name, out IReadOnlyList<StoredRow>? rows) ? rows : Array.Empty<StoredRow>();
    }

    // Runs one plan through the reader. An adapter would batch the queries of one round into one round trip.
    public void Fetch(IRecordReader reader, FactPlan plan)
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        if (plan == null || plan.IsEmpty)
        {
            return;
        }

        bool ran = false;
        foreach (FactQuery query in plan.Queries)
        {
            if (_facts.ContainsKey(query.Name))
            {
                continue;
            }

            if (!ran)
            {
                Rounds++;
                ran = true;
            }

            _queries.Add(query);
            IReadOnlyList<StoredRow> rows;
            if (query.Id != null)
            {
                StoredRow? row = reader.Get(query.Entity, query.Id);
                rows = row == null ? Array.Empty<StoredRow>() : new[] { row };
            }
            else if (query.Key != null)
            {
                StoredRow? row = reader.GetByKey(query.Entity, query.Key);
                rows = row == null ? Array.Empty<StoredRow>() : new[] { row };
            }
            else
            {
                rows = reader.Query(query.Query!);
            }

            _facts[query.Name] = rows;
        }
    }

    // Plan, fetch: the planner yields one plan per round and reads the facts fetched so far before it yields the next.
    public static FactSet Run(IRecordReader reader, Func<FactSet, IEnumerable<FactPlan>> planner)
    {
        if (planner == null)
        {
            throw new ArgumentNullException(nameof(planner));
        }

        var facts = new FactSet();
        foreach (FactPlan plan in planner(facts))
        {
            facts.Fetch(reader, plan);
        }

        return facts;
    }
}
