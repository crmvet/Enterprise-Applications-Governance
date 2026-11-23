using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Product.Azure.Stores.SqlServer;

// One migration script from ../sql, compiled into this assembly.
public sealed class SqlMigration
{
    internal SqlMigration(int version, string name, string text)
    {
        Version = version;
        Name = name;
        Text = text;
    }

    // The number the file name starts with: 1 for 0001-tables.sql.
    public int Version { get; }

    public string Name { get; }

    public string Text { get; }

    public override string ToString()
    {
        return Name;
    }
}

// Applies the Azure SQL scripts in file order and records each in host.SchemaVersion. Run by a deployment identity (the
// local server's migrate command, from the template's post-provision hook), never by the app: the app identity holds no
// DDL rights and only checks the version (SqlServerProductStore.EnsureSchema).
//
// Each script is a list of batches separated by GO lines, as sqlcmd reads them, and every statement in it checks before
// it acts, so a script that stops part way can run again from the start. A script is recorded once all its batches ran;
// a recorded script never runs again. Variables use the sqlcmd form $(Name); every variable a script names must be given.
public static class SqlServerMigrations
{
    private const string ResourcePrefix = "Product.Azure.Stores.SqlServer.Sql/";

    private static readonly Regex VariablePattern = new(@"\$\(([A-Za-z_][A-Za-z0-9_]*)\)", RegexOptions.CultureInvariant);

    // A variable value goes into SQL text inside [ ] and ' ', so it is limited to characters that cannot close either.
    private static readonly Regex SafeValue = new(@"^[A-Za-z0-9 _.@-]{0,128}$", RegexOptions.CultureInvariant);

    private static readonly Regex GoLine = new(@"^\s*GO\s*(--.*)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Lazy<IReadOnlyList<SqlMigration>> LazyAll = new(Load);

    private const string Bootstrap =
        "IF SCHEMA_ID(N'host') IS NULL EXEC (N'CREATE SCHEMA [host]');\n"
        + "IF OBJECT_ID(N'[host].[SchemaVersion]', N'U') IS NULL\n"
        + "    CREATE TABLE [host].[SchemaVersion]\n"
        + "    (\n"
        + "        [Version] int NOT NULL CONSTRAINT [PK_SchemaVersion] PRIMARY KEY,\n"
        + "        [Name] nvarchar(200) NOT NULL,\n"
        + "        [AppliedOn] datetime2(3) NOT NULL CONSTRAINT [DF_SchemaVersion_AppliedOn] DEFAULT (SYSUTCDATETIME())\n"
        + "    );";

    // Every script, by version.
    public static IReadOnlyList<SqlMigration> All => LazyAll.Value;

    // The version this build needs: the highest script number.
    public static int RequiredVersion => All.Count == 0 ? 0 : All.Max(m => m.Version);

    // The highest version recorded in host.SchemaVersion, or 0 before the first migration.
    public static int AppliedVersion(string connectionString)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        return AppliedVersion(connection);
    }

    public static void Apply(string connectionString, IReadOnlyDictionary<string, string> variables, TextWriter log)
    {
        if (variables == null)
        {
            throw new ArgumentNullException(nameof(variables));
        }

        log ??= TextWriter.Null;
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        Execute(connection, Bootstrap);
        int applied = AppliedVersion(connection);
        log.WriteLine("Schema version " + applied + "; this build needs " + RequiredVersion + ".");
        foreach (SqlMigration migration in All.Where(m => m.Version > applied))
        {
            IReadOnlyList<string> batches = SplitBatches(Substitute(migration.Text, variables));
            log.WriteLine("Applying " + migration.Name + " (" + batches.Count + " batches).");
            foreach (string batch in batches)
            {
                Execute(connection, batch);
            }

            using SqlCommand record = connection.CreateCommand();
            record.CommandText = "INSERT INTO [host].[SchemaVersion] ([Version], [Name]) VALUES (@version, @name);";
            record.Parameters.Add(new SqlParameter("@version", System.Data.SqlDbType.Int) { Value = migration.Version });
            record.Parameters.Add(new SqlParameter("@name", System.Data.SqlDbType.NVarChar, 200) { Value = migration.Name });
            record.ExecuteNonQuery();
            log.WriteLine("Applied " + migration.Name + ".");
        }

        log.WriteLine("Schema version " + AppliedVersion(connection) + ".");
    }

    // The batches of a script: the text between lines that hold only GO (any case, an optional comment after it). Batches
    // with nothing but blanks and comments are left out. The scripts never put GO inside a string or a block comment.
    public static IReadOnlyList<string> SplitBatches(string script)
    {
        if (script == null)
        {
            throw new ArgumentNullException(nameof(script));
        }

        var batches = new List<string>();
        var current = new StringBuilder();
        foreach (string line in script.Replace("\r\n", "\n").Split('\n'))
        {
            if (GoLine.IsMatch(line))
            {
                Flush();
            }
            else
            {
                current.Append(line).Append('\n');
            }
        }

        Flush();
        return new ReadOnlyCollection<string>(batches);

        void Flush()
        {
            string batch = current.ToString();
            current.Clear();
            bool hasCode = batch.Split('\n').Any(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("--", StringComparison.Ordinal));
            if (hasCode)
            {
                batches.Add(batch.TrimEnd('\n') + "\n");
            }
        }
    }

    // Replaces every $(Name) with its value. A variable the script names but the caller does not give, or a value with
    // characters that could close a bracket or a string, throws.
    public static string Substitute(string script, IReadOnlyDictionary<string, string> variables)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> variable in variables)
        {
            if (variable.Value == null || !SafeValue.IsMatch(variable.Value))
            {
                throw new ArgumentException("The value of SQL variable " + variable.Key + " may hold only letters, digits, spaces and the characters _ . @ -, up to 128.", nameof(variables));
            }

            lookup[variable.Key] = variable.Value;
        }

        return VariablePattern.Replace(script, match => lookup.TryGetValue(match.Groups[1].Value, out string? value)
            ? value
            : throw new ArgumentException("The script needs the SQL variable " + match.Groups[1].Value + ".", nameof(variables)));
    }

    // The variable names a script uses.
    public static IReadOnlyList<string> VariablesOf(string script)
    {
        return VariablePattern.Matches(script).Cast<Match>().Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    private static int AppliedVersion(SqlConnection connection)
    {
        using SqlCommand command = connection.CreateCommand();
        command.CommandText = "IF OBJECT_ID(N'[host].[SchemaVersion]', N'U') IS NULL SELECT 0 ELSE SELECT ISNULL(MAX([Version]), 0) FROM [host].[SchemaVersion];";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Execute(SqlConnection connection, string sql)
    {
        using SqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 600;
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<SqlMigration> Load()
    {
        Assembly assembly = typeof(SqlServerMigrations).Assembly;
        var migrations = new List<SqlMigration>();
        foreach (string resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
        {
            string name = resource.Substring(ResourcePrefix.Length);
            string file = name.Contains('/') ? name.Substring(name.LastIndexOf('/') + 1) : name;
            int digits = 0;
            while (digits < file.Length && char.IsDigit(file[digits]))
            {
                digits++;
            }

            if (digits == 0)
            {
                throw new InvalidOperationException("Migration " + name + " does not start with its version number.");
            }

            using Stream stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException("Migration " + name + " cannot be read.");
            using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            migrations.Add(new SqlMigration(int.Parse(file.Substring(0, digits), NumberStyles.None, CultureInfo.InvariantCulture), name, reader.ReadToEnd()));
        }

        if (migrations.Select(m => m.Version).Distinct().Count() != migrations.Count)
        {
            throw new InvalidOperationException("Two migrations share a version number.");
        }

        return new ReadOnlyCollection<SqlMigration>(migrations.OrderBy(m => m.Version).ToList());
    }
}
