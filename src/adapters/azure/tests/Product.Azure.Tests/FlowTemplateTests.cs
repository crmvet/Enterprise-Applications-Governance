using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Product.Azure.Host;
using Xunit;

namespace Product.Azure.Tests;

// The approval runner's flow template (src/adapters/azure/flows): the manifest and both workflows parse; every HTTP
// action calls only the runner's routes and the approval outcome route on the configured host; credentials come only
// from environment variables (the certificate and its password from Key Vault-backed secrets, read with secure inputs
// and outputs); no secret, tenant or client ID is written in a file; and each request body carries exactly the members
// its route takes. Nothing here imports or runs a flow.
public sealed class FlowTemplateTests
{
    private const string BaseUrl = "@{parameters('Approval API base URL ({{prefix}}_ApiBaseUrl)')}";

    private static readonly string[] Routes =
    {
        "/engine/v1/approval-runner/claims",
        "/engine/v1/approval-runner/items/{id}/sent",
        "/engine/v1/approval-runner/items/{id}/release",
        "/engine/v1/approval-records/{id}/outcome",
    };

    private static readonly string[] AllowedUrls =
    {
        "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
        "https://login.microsoftonline.com",
        "https://make.powerautomate.com/environments/",
    };

    private static string Folder => Path.Combine(TestPaths.Adapter, "flows");

    private static JsonObject Manifest => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Folder, "approval-runner", "solution.json")))!;

    [Fact]
    public void TheManifestAndBothWorkflowsParseAndNothingGeneratedIsCommitted()
    {
        JsonObject manifest = Manifest;
        string[] files = manifest["workflows"]!.AsArray().Select(w => w!["file"]!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "workflows/pull-approval-requests.json", "workflows/send-card-and-wait.json" }, files);
        foreach (string file in files)
        {
            JsonNode workflow = JsonNode.Parse(File.ReadAllText(Path.Combine(Folder, "approval-runner", file)))!;
            Assert.Equal("1.0.0.0", workflow["schemaVersion"]!.GetValue<string>());
            Assert.NotNull(workflow["properties"]!["definition"]!["actions"]);
        }

        Assert.Single(manifest["workflows"]!.AsArray(), w => w!["kind"]!.GetValue<string>() == "child");
        Assert.True(File.Exists(Path.Combine(Folder, "build-solution.mjs")));
        Assert.True(File.Exists(Path.Combine(Folder, "README.md")));
        string[] generated = Directory.GetFiles(Folder, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Empty(generated);
    }

    [Fact]
    public void EveryHttpActionCallsOnlyTheRunnerAndOutcomeRoutesOnTheConfiguredHost()
    {
        Assert.Equal(new[] { ApprovalRunnerRegistration.ClaimsRoute, ApprovalRunnerRegistration.SentRoute, ApprovalRunnerRegistration.ReleaseRoute }, Routes.Take(3));
        int calls = 0;
        foreach ((string file, string name, JsonObject action) in HttpActions())
        {
            calls++;
            JsonObject inputs = action["inputs"]!.AsObject();
            Assert.Equal("POST", inputs["method"]!.GetValue<string>());
            string uri = inputs["uri"]!.GetValue<string>();
            Assert.True(uri.StartsWith(BaseUrl, StringComparison.Ordinal), file + " " + name + " calls " + uri);
            string path = Regex.Replace(uri.Substring(BaseUrl.Length), @"@\{[^}]*\}", "{id}");
            Assert.True(Routes.Contains(path), file + " " + name + " calls " + path);
            Assert.Equal("application/json", inputs["headers"]!["Content-Type"]!.GetValue<string>());
        }

        Assert.Equal(7, calls);
    }

    [Fact]
    public void CredentialsComeOnlyFromEnvironmentVariablesAndNoSecretIsWritten()
    {
        JsonObject manifest = Manifest;
        var strings = manifest["environmentVariables"]!.AsArray().Where(v => v!["type"]!.GetValue<string>() == "string").Select(v => v!["schemaName"]!.GetValue<string>()).ToList();
        var secrets = manifest["environmentVariables"]!.AsArray().Where(v => v!["type"]!.GetValue<string>() == "secret").Select(v => v!["schemaName"]!.GetValue<string>()).ToList();
        Assert.Equal(new[] { "{{prefix}}_RunnerCertificate", "{{prefix}}_RunnerCertificatePassword" }, secrets);

        foreach ((string file, JsonObject definition) in Definitions())
        {
            Dictionary<string, JsonObject> actions = AllActions(definition).ToDictionary(a => a.Name, a => a.Action);

            // The secrets: read only with the Dataverse action Microsoft documents, from a declared secret variable, with
            // secure inputs and outputs.
            foreach (string reader in new[] { "Get_runner_certificate", "Get_certificate_password" })
            {
                JsonObject action = actions[reader];
                Assert.Equal("PerformUnboundAction", action["inputs"]!["host"]!["operationId"]!.GetValue<string>());
                Assert.Equal("RetrieveEnvironmentVariableSecretValue", action["inputs"]!["parameters"]!["actionName"]!.GetValue<string>());
                Assert.Contains(action["inputs"]!["parameters"]!["item/EnvironmentVariableName"]!.GetValue<string>(), secrets);
                Assert.Equal(new[] { "inputs", "outputs" }, Secure(action));
            }

            foreach ((string _, string name, JsonObject action) in HttpActions().Where(h => h.File == file))
            {
                JsonObject auth = action["inputs"]!["authentication"]!.AsObject();
                Assert.Equal("ActiveDirectoryOAuth", auth["type"]!.GetValue<string>());
                Assert.Equal(new[] { "audience", "authority", "clientId", "password", "pfx", "tenant", "type" }, auth.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal));
                foreach (string member in new[] { "tenant", "audience", "clientId" })
                {
                    Match parameter = Regex.Match(auth[member]!.GetValue<string>(), @"^@parameters\('(?<name>[^']* \((?<schema>[^)]+)\))'\)$");
                    Assert.True(parameter.Success, name + " " + member);
                    Assert.Contains(parameter.Groups["schema"].Value, strings);
                    Assert.Equal(parameter.Groups["schema"].Value, definition["parameters"]![parameter.Groups["name"].Value]!["metadata"]!["schemaName"]!.GetValue<string>());
                }

                Assert.Equal("@body('Get_runner_certificate')?['EnvironmentVariableSecretValue']", auth["pfx"]!.GetValue<string>());
                Assert.Equal("@body('Get_certificate_password')?['EnvironmentVariableSecretValue']", auth["password"]!.GetValue<string>());
                Assert.Contains("inputs", Secure(action));
            }
        }

        // No secret, ID or foreign address in any file of the template.
        foreach (string path in Directory.GetFiles(Folder, "*.json", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(path);
            Assert.DoesNotMatch(new Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"), text);
            Assert.DoesNotMatch(new Regex("[A-Za-z0-9+/=]{80,}"), text);
            Assert.DoesNotContain("BEGIN", text, StringComparison.Ordinal);
            Assert.DoesNotMatch(new Regex("\"(secret|clientSecret|password)\"\\s*:\\s*\"[^@]"), text);
            Assert.DoesNotContain("client_secret", text, StringComparison.OrdinalIgnoreCase);
            if (!path.EndsWith("solution.json", StringComparison.Ordinal))
            {
                foreach (Match url in Regex.Matches(text, "https://[^\"' ,)]*"))
                {
                    Assert.True(AllowedUrls.Any(a => url.Value.StartsWith(a, StringComparison.Ordinal) && (a.EndsWith("/", StringComparison.Ordinal) || url.Value == a)), Path.GetFileName(path) + " names " + url.Value);
                }
            }
        }
    }

    [Fact]
    public void EachRequestBodyCarriesExactlyTheMembersItsRouteTakes()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string file, string name, JsonObject action) in HttpActions())
        {
            string uri = action["inputs"]!["uri"]!.GetValue<string>();
            string[] members = action["inputs"]!["body"]!.AsObject().Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            if (uri.EndsWith("/claims", StringComparison.Ordinal))
            {
                Assert.All(members, m => Assert.Contains(m, ApprovalRunnerEndpoints.ClaimMembers));
                seen.Add("claims");
            }
            else if (uri.EndsWith("/sent", StringComparison.Ordinal))
            {
                Assert.Equal(ApprovalRunnerEndpoints.SentMembers.OrderBy(m => m, StringComparer.Ordinal), members);
                seen.Add("sent");
            }
            else if (uri.EndsWith("/release", StringComparison.Ordinal))
            {
                Assert.Equal(ApprovalRunnerEndpoints.ReleaseMembers.OrderBy(m => m, StringComparer.Ordinal), members);
                Assert.Contains(action["inputs"]!["body"]!["reason"]!.GetValue<string>(), ApprovalRunnerEndpoints.ReleaseReasons);
                seen.Add("release");
            }
            else
            {
                Assert.Equal(ApprovalOutcomeEndpoint.AllowedMembers.OrderBy(m => m, StringComparer.Ordinal), members);
                string key = action["inputs"]!["headers"]![ApprovalOutcomeEndpoint.IdempotencyHeader]!.GetValue<string>();
                Assert.Equal("@{outputs('Item')?['approvalRecordId']}:@{outputs('First_response')?['responder']?['id']}:@{outputs('First_response')?['responseDate']}", key);
                Assert.Equal("@{outputs('First_response')?['responseDate']}", action["inputs"]!["body"]!["respondedOn"]!.GetValue<string>());
                Assert.Equal("@outputs('First_response')?['responder']?['id']", action["inputs"]!["body"]!["approver"]!.GetValue<string>());
                seen.Add("outcome");
            }
        }

        Assert.Equal(new[] { "claims", "outcome", "release", "sent" }, seen.OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public void TheCardAsksOnlyTheResolvedUsersFirstToRespondWithoutReassignment()
    {
        JsonObject child = Definitions().Single(d => d.File.EndsWith("send-card-and-wait.json", StringComparison.Ordinal)).Definition;
        Dictionary<string, JsonObject> actions = AllActions(child).ToDictionary(a => a.Name, a => a.Action);
        JsonObject parameters = actions["Create_an_approval"]["inputs"]!["parameters"]!.AsObject();
        Assert.Equal("CreateAnApproval", actions["Create_an_approval"]["inputs"]!["host"]!["operationId"]!.GetValue<string>());
        Assert.Equal("Basic", parameters["approvalType"]!.GetValue<string>());
        Assert.False(parameters["ApprovalCreationInput/enableReassignment"]!.GetValue<bool>());
        Assert.Equal("@join(variables('AskedUsers'), ';')", parameters["ApprovalCreationInput/assignedTo"]!.GetValue<string>());
        Assert.Equal("@equals(item()?['@odata.type'], '#microsoft.graph.user')", actions["Keep_users_only"]["inputs"]!["where"]!.GetValue<string>());
        Assert.Equal("P27D", actions["Wait_for_an_approval"]["limit"]!["timeout"]!.GetValue<string>());

        // Every connector call uses a connection reference the solution declares.
        JsonObject manifest = Manifest;
        string[] declared = manifest["connectionReferences"]!.AsArray().Select(r => r!["logicalName"]!.GetValue<string>()).ToArray();
        foreach ((string file, JsonObject definition) in Definitions())
        {
            JsonObject workflow = (JsonObject)JsonNode.Parse(File.ReadAllText(file))!;
            JsonObject references = workflow["properties"]!["connectionReferences"]!.AsObject();
            foreach (KeyValuePair<string, JsonNode?> reference in references)
            {
                Assert.Equal("embedded", reference.Value!["runtimeSource"]!.GetValue<string>());
                Assert.Contains(reference.Value["connection"]!["connectionReferenceLogicalName"]!.GetValue<string>(), declared);
            }

            foreach ((string name, JsonObject action) in AllActions(definition).Where(a => a.Action["type"]!.GetValue<string>().StartsWith("OpenApiConnection", StringComparison.Ordinal)))
            {
                Assert.True(references.ContainsKey(action["inputs"]!["host"]!["connectionName"]!.GetValue<string>()), name);
            }

            string[] types = AllActions(definition).Select(a => a.Action["type"]!.GetValue<string>()).Distinct().ToArray();
            Assert.All(types, t => Assert.Contains(t, new[] { "OpenApiConnection", "OpenApiConnectionWebhook", "Http", "Workflow", "Foreach", "If", "Compose", "Select", "Query", "InitializeVariable", "SetVariable", "Response", "Terminate" }));
        }
    }

    // ---- Reading the workflows ----

    private static IEnumerable<(string File, JsonObject Definition)> Definitions()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(Folder, "approval-runner", "workflows"), "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            yield return (file, JsonNode.Parse(File.ReadAllText(file))!["properties"]!["definition"]!.AsObject());
        }
    }

    private static IEnumerable<(string File, string Name, JsonObject Action)> HttpActions()
    {
        foreach ((string file, JsonObject definition) in Definitions())
        {
            foreach ((string name, JsonObject action) in AllActions(definition).Where(a => a.Action["type"]!.GetValue<string>() == "Http"))
            {
                yield return (file, name, action);
            }
        }
    }

    // Every action, also inside loops and conditions.
    private static IEnumerable<(string Name, JsonObject Action)> AllActions(JsonObject definition)
    {
        var stack = new Stack<JsonObject>();
        stack.Push(definition["actions"]!.AsObject());
        while (stack.Count > 0)
        {
            foreach (KeyValuePair<string, JsonNode?> entry in stack.Pop())
            {
                JsonObject action = entry.Value!.AsObject();
                yield return (entry.Key, action);
                if (action["actions"] is JsonObject inner)
                {
                    stack.Push(inner);
                }

                if (action["else"]?["actions"] is JsonObject otherwise)
                {
                    stack.Push(otherwise);
                }
            }
        }
    }

    private static string[] Secure(JsonObject action)
    {
        return action["runtimeConfiguration"]?["secureData"]?["properties"]?.AsArray().Select(p => p!.GetValue<string>()).ToArray() ?? Array.Empty<string>();
    }
}
