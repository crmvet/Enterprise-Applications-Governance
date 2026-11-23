using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Product.Azure.Host;
using Product.Azure.Stores;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Runtime;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;

namespace Product.Azure.Tests;

// One exchange of the walkthrough, replayed on both sides.
internal sealed class ReplayedExchange
{
    public int N { get; set; }

    public string Label { get; set; } = string.Empty;

    // The outcome digest the checked-in transcript holds.
    public string Transcript { get; set; } = string.Empty;

    // The outcome digest of the in-process run (the MCP core and engine on the in-memory store).
    public string InProcess { get; set; } = string.Empty;

    // The outcome digest over HTTP (the local server on SQLite).
    public string Http { get; set; } = string.Empty;

    // The canonical views of the answer (masked correlation IDs), when the exchange is compared in full.
    public string? InProcessView { get; set; }

    public string? HttpView { get; set; }

    // The answer the transcript recorded, and the in-process answer in the same (slim) form, for the raw channel.
    public string? TranscriptView { get; set; }

    public string? InProcessRecordedView { get; set; }

    public int? InProcessStatus { get; set; }

    public int? HttpStatus { get; set; }

    // The HTTP status of a REST endpoint call (the approval outcome endpoint, the record endpoint).
    public int? EndpointStatus { get; set; }

    public bool DigestsMatch => Transcript == InProcess && InProcess == Http;

    public bool ViewsMatch => InProcessView == HttpView && InProcessStatus == HttpStatus;

    public override string ToString()
    {
        return "#" + N + " " + Label + "\n  transcript: " + Transcript + "\n  in process: " + InProcess + "\n  over HTTP:  " + Http
            + (ViewsMatch ? string.Empty : "\n  in-process answer (" + InProcessStatus + "): " + Clip(InProcessView) + "\n  HTTP answer (" + HttpStatus + "):       " + Clip(HttpView));
    }

    private static string Clip(string? text)
    {
        return text == null ? "(none)" : text.Length <= 3000 ? text : text.Substring(0, 3000) + "...";
    }
}

// The walkthrough transcript (src/demo/walkthrough/transcript.json) replayed exchange by exchange, in order, against the
// HTTP host and against a fresh in-process world seeded the same way, with both clocks set to each exchange's time:
// MCP exchanges through an MCP client channel; the runner's seed into both stores; an AI client's table writes through
// each side's own engine runtime; the approval runner's answer through the approval outcome endpoint; the pipeline's
// runs through the record endpoint.
internal static class WalkthroughReplay
{
    public static JsonObject LoadTranscript()
    {
        return JsonNode.Parse(File.ReadAllText(TestPaths.Transcript, Encoding.UTF8))!.AsObject();
    }

    public static async Task<List<ReplayedExchange>> Run(ProductTestHost host, InProcessWorld world, IMcpChannel channel)
    {
        JsonObject transcript = LoadTranscript();
        var results = new List<ReplayedExchange>();
        foreach (JsonNode? node in transcript["exchanges"]!.AsArray())
        {
            JsonObject x = node!.AsObject();
            DateTime at = DateTime.Parse(x["at"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            host.Clock.UtcNow = at;
            world.Clock.UtcNow = at;
            string channelName = x["channel"]!.GetValue<string>();
            string entryPoint = x["entryPoint"]!.GetValue<string>();
            var result = new ReplayedExchange
            {
                N = x["n"]!.GetValue<int>(),
                Label = "step " + x["step"] + " " + channelName + " " + entryPoint,
                Transcript = JsonText.Canonical(x["outcome"]),
            };

            switch (channelName)
            {
                case "mcp":
                    await Mcp(host, world, channel, x, result);
                    break;
                case "seed":
                    Seed(host, world, x["request"]!["rows"]!.AsObject());
                    result.InProcess = result.Http = JsonText.Canonical(JsonText.Digest(true, null, Array.Empty<string>()));
                    break;
                case "engine" when entryPoint.StartsWith("table write", StringComparison.Ordinal):
                    TableWrite(host, world, x, result);
                    break;
                case "engine" when entryPoint.StartsWith("approval writer", StringComparison.Ordinal):
                    await ApprovalAnswer(host, world, x, result);
                    break;
                case "record-endpoint":
                    await RecordRun(host, world, x, result);
                    break;
                default:
                    throw new InvalidOperationException("The replay does not know the exchange " + result.Label + ".");
            }

            results.Add(result);
        }

        return results;
    }

    // Both stores' committed rows as InMemoryStore.Dump writes them, with correlation IDs masked (each side numbers its
    // own requests).
    public static (string InProcess, string Http) Dumps(ProductTestHost host, InProcessWorld world)
    {
        string http = ((RowStore)host.Get<IProductStore>()).Dump();
        return (Mask(world.Store.Dump()), Mask(http));
    }

    private static string Mask(string dump)
    {
        return Regex.Replace(dump, "\"correlationId\":\"[^\"]*\"", "\"correlationId\":\"" + JsonText.Masked + "\"");
    }

    private static async Task Mcp(ProductTestHost host, InProcessWorld world, IMcpChannel channel, JsonObject x, ReplayedExchange result)
    {
        string token = x["request"]!["headers"]!["Authorization"]!.GetValue<string>().Substring("Bearer ".Length);
        Persona persona = Personas.ForMockToken(token);
        JsonNode message = x["request"]!["body"]!;
        string method = message["method"]!.GetValue<string>();

        (int status, string? body) = world.Handle(persona, message);
        JsonNode? inProcessBody = body == null ? null : JsonNode.Parse(body);
        McpSeen inProcess = channel.ViewOf(status, inProcessBody, method);
        McpSeen http = await channel.Send(persona, message);

        result.InProcess = JsonText.Canonical(inProcess.Digest);
        result.Http = JsonText.Canonical(http.Digest);
        result.InProcessStatus = inProcess.Status;
        result.HttpStatus = http.Status;
        result.InProcessView = JsonText.Canonical(inProcess.View);
        result.HttpView = JsonText.Canonical(http.View);

        // The transcript keeps a slim tools/list (names, titles, annotations, schema sizes); compare the same slim form.
        JsonNode? recorded = x["response"]?["body"];
        JsonNode? inProcessForTranscript = method == "tools/list" ? Slim(inProcessBody) : inProcessBody;
        if (channel is RawMcpChannel)
        {
            result.TranscriptView = JsonText.Canonical(recorded);
            result.InProcessRecordedView = JsonText.Canonical(inProcessForTranscript);
        }
    }

    private static void Seed(ProductTestHost host, InProcessWorld world, JsonObject rows)
    {
        IStoreSeeder seeder = host.Get<IStoreSeeder>();
        IRecordStore records = host.Get<IRecordStore>();
        foreach (KeyValuePair<string, JsonNode?> row in rows)
        {
            string[] parts = row.Key.Split(' ');
            string entity = parts[0];
            string id = parts[1];
            JsonTreeNode columns = CanonicalJson.Parse(row.Value!.ToJsonString());
            if (world.Store.Get(entity, id) != null)
            {
                world.Store.Tamper(entity, id, columns);
            }
            else
            {
                world.Store.Seed(entity, columns, id);
            }

            if (records.Get(entity, id) != null)
            {
                seeder.Replace(entity, id, columns);
            }
            else
            {
                seeder.Seed(entity, columns, id);
            }
        }
    }

    // An AI client tries the engine's table door directly: the same write through each side's own engine runtime.
    private static void TableWrite(ProductTestHost host, InProcessWorld world, JsonObject x, ReplayedExchange result)
    {
        JsonNode caller = x["request"]!["caller"]!;
        JsonNode body = x["request"]!["body"]!;
        CallerIdentity ai = CallerIdentity.AiClient(
            caller["principalId"]!.GetValue<string>(),
            caller["groups"]!.AsArray().Select(g => g!.GetValue<string>()).ToArray(),
            caller["clientApp"]!.GetValue<string>(),
            clientAppVerified: true);
        WriteCommand command = WriteCommand.Update(body["entity"]!.GetValue<string>(), body["rowId"]!.GetValue<string>(), body["after"]!.ToJsonString());
        RuntimeOutcome inProcess = world.Runtime.Write(ai, command);
        RuntimeOutcome http = host.Get<EngineRuntime>().Write(ai, command);
        result.InProcess = JsonText.Canonical(JsonText.EngineDigest(inProcess, inProcess.Succeeded ? null : ProductErrorMap.Map(inProcess, ContractSet.Embedded, aiCaller: true)));
        result.Http = JsonText.Canonical(JsonText.EngineDigest(http, http.Succeeded ? null : ProductErrorMap.Map(http, ContractSet.Embedded, aiCaller: true)));
    }

    // The approval runner writes the business owner's answer: in process as the runner's workload identity, over HTTP
    // through the approval outcome endpoint with the runner's app-only persona.
    private static async Task ApprovalAnswer(ProductTestHost host, InProcessWorld world, JsonObject x, ReplayedExchange result)
    {
        JsonNode caller = x["request"]!["caller"]!;
        JsonNode body = x["request"]!["body"]!;
        string id = body["rowId"]!.GetValue<string>();
        CallerIdentity runner = CallerIdentity.Workload(caller["principalId"]!.GetValue<string>(), clientApp: caller["clientApp"]!.GetValue<string>());
        RuntimeOutcome inProcess = world.Runtime.Write(runner, WriteCommand.Update("approval-record", id, body["after"]!.ToJsonString()));
        result.InProcess = JsonText.Canonical(JsonText.EngineDigest(inProcess, inProcess.Succeeded ? null : ProductErrorMap.Map(inProcess, ContractSet.Embedded, aiCaller: false)));

        HttpCall call = await host.Post("/engine/v1/approval-records/" + id + "/outcome", Personas.Runner, body["after"]!.ToJsonString());
        result.EndpointStatus = call.Status;
        result.Http = JsonText.Canonical(JsonText.EnvelopeDigest(call.Json));
    }

    // The release pipeline records a run: in process through the engine entry the endpoint calls, over HTTP through the
    // record endpoint with the pipeline's app-only persona.
    private static async Task RecordRun(ProductTestHost host, InProcessWorld world, JsonObject x, ReplayedExchange result)
    {
        JsonNode caller = x["request"]!["caller"]!;
        JsonNode body = x["request"]!["body"]!;
        CallerIdentity pipeline = CallerIdentity.Workload(caller["principalId"]!.GetValue<string>(), clientApp: caller["clientApp"]!.GetValue<string>());
        EngineContract.CodeApprovalKinds.TryParse(body["codeApprovalKind"]?.GetValue<string>() ?? "unknown", out CodeApprovalKind codeApproval);
        var report = new DeploymentReport(body["idempotencyKey"]!.GetValue<string>(), body["businessApplicationId"]!.GetValue<string>(), body["artifactName"]!.GetValue<string>(), body["artifactVersion"]!.GetValue<string>(), Utc(body["finishedOn"]!))
        {
            ArtifactKindKey = body["artifactKindKey"]?.GetValue<string>(),
            TargetId = body["deploymentTargetId"]?.GetValue<string>(),
            ChangeRequestId = body["changeRequestId"]?.GetValue<string>(),
            RunUrl = body["runUrl"]?.GetValue<string>(),
            StartedOnUtc = body["startedOn"] == null ? null : Utc(body["startedOn"]!),
            ResultMeaning = body["resultMeaning"]?.GetValue<string>() ?? "succeeded",
            CodeApprovalKind = codeApproval,
        };
        RuntimeOutcome inProcess = world.Runtime.RecordDeployment(pipeline, report);
        result.InProcess = JsonText.Canonical(JsonText.EngineDigest(inProcess, inProcess.Succeeded ? null : ProductErrorMap.Map(inProcess, ContractSet.Embedded, aiCaller: false)));

        HttpCall call = await host.Post("/api/deployments", Personas.Pipeline, body.ToJsonString());
        result.EndpointStatus = call.Status;
        result.Http = JsonText.Canonical(JsonText.EnvelopeDigest(call.Json));
    }

    // The tools/list answer as the walkthrough recorder slims it.
    private static JsonNode? Slim(JsonNode? body)
    {
        if (body?["result"]?["tools"] is not JsonArray tools)
        {
            return body;
        }

        JsonNode copy = body.DeepClone();
        var slim = new JsonArray();
        foreach (JsonNode? tool in tools)
        {
            slim.Add(new JsonObject
            {
                ["name"] = tool!["name"]!.GetValue<string>(),
                ["title"] = tool["title"]?.DeepClone(),
                ["annotations"] = tool["annotations"]?.DeepClone(),
                ["schemaCharacters"] = (tool["inputSchema"]?.ToJsonString().Length ?? 0) + (tool["outputSchema"]?.ToJsonString().Length ?? 0),
            });
        }

        copy["result"]!["tools"] = slim;
        copy["result"]!["note"] = "inputSchema, outputSchema and description of each tool are left out of this transcript; schemaCharacters gives their size.";
        return copy;
    }

    private static DateTime Utc(JsonNode node)
    {
        return DateTime.Parse(node.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }
}
