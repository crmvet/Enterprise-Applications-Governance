using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Product.Azure.Server;
using Xunit;

namespace Product.Azure.Tests;

// The SQLite store across a host restart: a file seeded by the local server's seed command, written through one host,
// read through a second host on the same file. Row IDs are random here, as in a real local run.
public sealed class PersistenceTests
{
    [Fact]
    public async Task DataWrittenThroughOneHostIsThereAfterARestart()
    {
        string folder = TestPaths.NewScratchFolder();
        try
        {
            string file = Path.Combine(folder, "product.db");
            Assert.Equal(0, Seed(file, out string seeded));
            Assert.Contains("version 1", seeded, StringComparison.Ordinal);
            Assert.Equal(1, Seed(file, out string again));
            Assert.Contains("already has a configuration version", again, StringComparison.Ordinal);

            const string intakeArguments = "{\"title\":\"Crew overtime tracker\",\"need\":\"Crew leads track overtime in a spreadsheet that nobody owns.\",\"proposedKindKey\":\"power-platform-app\",\"clientRequestId\":\"restart-0001\"}";
            string intake;
            string card;
            string status;
            string info;
            await using (ProductTestHost first = ProductTestHost.Start(new HostSettings { DatabaseFile = file, SequentialIds = false, Seed = false }))
            {
                JsonNode draft = await first.Call(Personas.Priya, "save_intake_draft", intakeArguments);
                Assert.True(draft["ok"]!.GetValue<bool>(), draft.ToJsonString() + "\n" + first.Logs);
                intake = draft["data"]!["recordId"]!.GetValue<string>();
                JsonNode submitted = await first.Call(Personas.Priya, "submit_record", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\"}");
                Assert.True(submitted["ok"]!.GetValue<bool>(), submitted.ToJsonString());
                card = JsonText.Canonical(await first.Call(Personas.Mateo, "get_application_card", "{\"businessApplicationId\":\"" + Personas.InspectionScheduler + "\"}"));
                status = JsonText.Canonical(await first.Call(Personas.Priya, "get_approval_status", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\"}"));
                info = JsonText.Canonical((await first.Get("/api/info", Personas.Priya)).Json!["configuration"]);
            }

            SqliteConnection.ClearAllPools();
            await using (ProductTestHost second = ProductTestHost.Start(new HostSettings { DatabaseFile = file, SequentialIds = false, Seed = false }))
            {
                Assert.Equal(card, JsonText.Canonical(await second.Call(Personas.Mateo, "get_application_card", "{\"businessApplicationId\":\"" + Personas.InspectionScheduler + "\"}")));
                JsonNode afterRestart = await second.Call(Personas.Priya, "get_approval_status", "{\"subjectKind\":\"intake-request\",\"subjectId\":\"" + intake + "\"}");
                Assert.Equal(status, JsonText.Canonical(afterRestart));
                Assert.Equal(info, JsonText.Canonical((await second.Get("/api/info", Personas.Priya)).Json!["configuration"]));
                Assert.Equal("submitted", second.Get<Product.Engine.Runtime.Ports.IRecordStore>().Get("intake-request", intake)!.Text("statusMeaning"));

                // The same clientRequestId after the restart returns the first result and writes nothing new.
                int before = second.Get<Product.Engine.Runtime.Ports.IRecordStore>().Query(Product.Engine.Runtime.Ports.RowQuery.For("intake-request")).Count;
                JsonNode replayed = await second.Call(Personas.Priya, "save_intake_draft", intakeArguments);
                Assert.True(replayed["ok"]!.GetValue<bool>(), replayed.ToJsonString());
                Assert.Equal(intake, replayed["data"]!["recordId"]!.GetValue<string>());
                Assert.Contains(replayed["warnings"]!.AsArray(), w => w!["code"]?.GetValue<string>() == "request.replayed");
                Assert.Equal(before, second.Get<Product.Engine.Runtime.Ports.IRecordStore>().Query(Product.Engine.Runtime.Ports.RowQuery.For("intake-request")).Count);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TestPaths.TryDelete(folder);
        }
    }

    [Fact]
    public void TheSeedCommandRefusesTheProductionEnvironment()
    {
        string folder = TestPaths.NewScratchFolder();
        try
        {
            using var output = new StringWriter();
            int code = ServerCommands.Seed(new[] { "--environment=Production", "--Product:Store:Provider=sqlite", "--Product:Store:Sqlite:ConnectionString=Data Source=" + Path.Combine(folder, "product.db") }, output);
            Assert.Equal(1, code);
            Assert.Contains("refused in the Production environment", output.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(folder, "product.db")));
        }
        finally
        {
            TestPaths.TryDelete(folder);
        }
    }

    // The local server's seed command on the file: Balanced, the demo binding and the walkthrough estate.
    private static int Seed(string file, out string output)
    {
        using var writer = new StringWriter();
        int code = ServerCommands.Seed(new[] { "--validated-on", "2026-11-22", "--environment=Development", "--Product:Store:Provider=sqlite", "--Product:Store:Sqlite:ConnectionString=Data Source=" + file }, writer);
        output = writer.ToString();
        SqliteConnection.ClearAllPools();
        return code;
    }
}
