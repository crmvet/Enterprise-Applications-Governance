using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Model;
using Xunit;

namespace Product.Engine.Core.Tests;

// The public, read-only view of the logical model compiled into the engine (LogicalModel.Compiled). The oracle is
// src/model/logical-model.json itself, read here from the file, so the compiled-in view is checked against the source.
public sealed class LogicalModelViewTests
{
    private static readonly Lazy<JsonTreeNode> Source = new(() => CanonicalJson.Parse(File.ReadAllText(Path.Combine(FindSource(), "model", "logical-model.json"))));

    [Fact]
    public void EveryEntityOfTheModelIsInTheView()
    {
        LogicalModel model = LogicalModel.Compiled;
        IReadOnlyList<JsonTreeNode> entities = Items(Source.Value, "entities");

        Assert.Equal(81, model.Entities.Count);
        Assert.Equal(entities.Count, model.Entities.Count);
        Assert.Equal(Text(Source.Value, "modelVersion"), model.ModelVersion);
        for (int i = 0; i < entities.Count; i++)
        {
            LogicalEntity entity = model.Entities[i];
            string name = Text(entities[i], "name");
            Assert.Equal(Text(entities[i], "id"), entity.Id);
            Assert.Equal(name, entity.Name);
            Assert.Equal(string.Join("-", name.Split(' ').Select(w => w.ToLowerInvariant())), entity.Key);
            Assert.Equal(Text(entities[i], "historyClass"), entity.HistoryClass);
            Assert.Equal(Text(entities[i], "kind"), entity.Kind);
            Assert.Equal(Text(entities[i], "deletePolicy"), entity.DeletePolicy);
            Assert.Same(entity, model.Get(entity.Key));
            Assert.True(model.TryGet(entity.Key, out LogicalEntity? found));
            Assert.Same(entity, found);
        }

        Assert.False(model.TryGet("no-such-entity", out _));
        Assert.Throws<KeyNotFoundException>(() => model.Get("no-such-entity"));
    }

    [Fact]
    public void ColumnsAndKeysFollowTheModel()
    {
        LogicalEntity approval = LogicalModel.Compiled.Get("approval-record");
        Assert.Equal("O10", approval.Id);
        Assert.Equal("F", approval.HistoryClass);

        // The common column set first, then the entity's own attributes, each contract name once.
        Assert.Equal("id", approval.Columns[0].ContractName);
        Assert.Equal("common", approval.Columns[0].ColumnSet);
        Assert.Equal(approval.Columns.Count, approval.Columns.Select(c => c.ContractName).Distinct(StringComparer.Ordinal).Count());
        Assert.True(approval.TryGetColumn("approverPrincipalKind", out LogicalColumn? kind));
        Assert.Equal("Approver Principal Kind", kind!.LogicalName);
        Assert.Null(kind.ColumnSet);
        Assert.False(approval.TryGetColumn("ApproverPrincipalKind", out _));

        JsonTreeNode source = Items(Source.Value, "entities").Single(e => Text(e, "id") == "O10");
        foreach (JsonTreeNode attribute in Items(source, "attributes"))
        {
            LogicalColumn column = approval.Columns.Single(c => c.LogicalName == Text(attribute, "name"));
            Assert.Equal(Text(attribute, "type"), column.Type);
            Assert.Equal(Text(attribute, "required"), column.Required);
            Assert.Equal(Text(attribute, "required") == "Y", column.IsRequired);
        }

        LogicalUniqueKey runUrl = Assert.Single(approval.UniqueKeys);
        Assert.Equal("alternate", runUrl.Kind);
        Assert.Equal(new[] { "Flow Run Url" }, runUrl.Columns);
        Assert.Equal(new[] { "flowRunUrl" }, runUrl.ContractColumns);
        Assert.Equal("engine", runUrl.Enforcement);
        Assert.Equal("Once answered", runUrl.Condition);

        LogicalUniqueKey idempotency = Assert.Single(LogicalModel.Compiled.Get("deployment-record").UniqueKeys);
        Assert.Equal(new[] { "idempotencyKey" }, idempotency.ContractColumns);
        Assert.Equal("store", idempotency.Enforcement);
        Assert.Null(idempotency.Condition);

        // O24 restates three common columns; each still appears once, with the entity's own definition.
        LogicalEntity audit = LogicalModel.Compiled.Get("audit-entry");
        Assert.Equal(audit.Columns.Count, audit.Columns.Select(c => c.ContractName).Distinct(StringComparer.Ordinal).Count());
        Assert.True(audit.TryGetColumn("onBehalfOf", out LogicalColumn? onBehalfOf));
        Assert.Null(onBehalfOf!.ColumnSet);

        // C03 Preset is stored inside C01.
        Assert.Equal("C01", LogicalModel.Compiled.Get("preset").StoredIn);
        Assert.Null(approval.StoredIn);
    }

    [Fact]
    public void TheViewCannotBeChanged()
    {
        LogicalModel model = LogicalModel.Compiled;
        LogicalEntity approval = model.Get("approval-record");

        Assert.Throws<NotSupportedException>(() => ((IList<LogicalEntity>)model.Entities).Add(approval));
        Assert.Throws<NotSupportedException>(() => ((IList<LogicalEntity>)model.Entities).RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => ((IList<LogicalColumn>)approval.Columns).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<LogicalUniqueKey>)approval.UniqueKeys).RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)approval.UniqueKeys[0].Columns)[0] = "Other");
        Assert.Throws<NotSupportedException>(() => ((IList<string>)approval.UniqueKeys[0].ContractColumns).Add("other"));

        // The same instance every time: the model is parsed once.
        Assert.Same(model, LogicalModel.Compiled);
        Assert.Equal(81, LogicalModel.Compiled.Entities.Count);
    }

    // src/: from the current folder (the test apps run there when built outside the tree), then from this assembly.
    private static string FindSource()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? dir = new(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "model", "logical-model.json")) && Directory.Exists(Path.Combine(dir.FullName, "engine")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }

        throw new DirectoryNotFoundException("Cannot find src/: no parent folder holds model/logical-model.json.");
    }

    private static IReadOnlyList<JsonTreeNode> Items(JsonTreeNode node, string member)
    {
        return node.TryGetMember(member, out JsonTreeNode? value) && value.Kind == JsonTreeKind.Array ? value.Items : Array.Empty<JsonTreeNode>();
    }

    private static string Text(JsonTreeNode node, string member)
    {
        return node.TryGetMember(member, out JsonTreeNode? value) && value.Kind == JsonTreeKind.String ? value.GetString() : string.Empty;
    }
}
