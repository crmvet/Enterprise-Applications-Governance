using System;
using System.Collections.Generic;
using System.Linq;
using Product.Engine.Configuration.Activation;
using Product.Engine.Configuration.Composition;
using Product.Engine.Configuration.Patches;
using Product.Engine.Configuration.Snapshots;
using Product.Engine.Configuration.Validation;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Operations;
using Product.Engine.Core.Reasons;
using Product.Engine.Runtime.Callers;
using Product.Engine.Runtime.Ports;
using Product.Engine.Runtime.Snapshots;

namespace Product.Engine.Runtime;

// <prefix>_ProposeConfigurationChange (document 25, section 11.4; document 21, section 5.1, step 4).
public sealed class ConfigurationProposal
{
    public ConfigurationProposal(JsonTreeNode changeSet, int baseVersionNumber, string proposalRationale)
    {
        ChangeSet = changeSet ?? throw new ArgumentNullException(nameof(changeSet));
        BaseVersionNumber = baseVersionNumber;
        ProposalRationale = proposalRationale ?? throw new ArgumentNullException(nameof(proposalRationale));
    }

    // The patch document: upsert and retire operations by logical entity and key (document 21, section 8.2).
    public JsonTreeNode ChangeSet { get; }

    public int BaseVersionNumber { get; }

    public string ProposalRationale { get; }

    // To edit an unsubmitted draft.
    public string? ConfigurationDraftId { get; set; }

    public bool ValidateOnly { get; set; }

    public string? IdempotencyKey { get; set; }
}

public sealed partial class EngineRuntime
{
    // The bootstrap of version 1 (document 21, section 4.8): the person behind the call, with the platform's install
    // rights, while no version exists. Every write the activator plans passes OP-08 against version 1, as an engine-nested
    // write of the activate-configuration effect, and the accepted decision under the bootstrap basis (G-17).
    public RuntimeOutcome Bootstrap(CallerIdentity caller, string presetKey, IEnumerable<string>? overlays, bool hasInstallRights)
    {
        if (caller == null)
        {
            throw new ArgumentNullException(nameof(caller));
        }

        DateTime now = Now();
        InForceVersion? existing = _configuration.ProbeInForceVersion();
        CallerResolution resolution = CallerResolver.ResolveWithoutConfiguration(caller);
        if (resolution.Actor == null)
        {
            return new RuntimeOutcome(OperationOutcomes.Refused, new[] { resolution.Refusal! }, null, null);
        }

        using IStoreTransaction tx = _records.Begin();
        var call = new RuntimeCall(tx, null, resolution.Actor, caller, now, _correlationIds());
        ActivationOutcome outcome = _activator.Bootstrap(
            call.Context(),
            new BootstrapRequest(presetKey, overlays, existing == null, hasInstallRights, PrincipalKinds(tx)));
        call.Results["bootstrap"] = RuntimeJson.With(outcome.ToJson(), "outcome", JsonTreeNode.FromString(outcome.Outcome));
        if (outcome.Outcome != ActivationOutcome.Bootstrapped)
        {
            string word = outcome.Outcome == ActivationOutcome.InvalidInput ? OperationOutcomes.InvalidInput : OperationOutcomes.Refused;
            return new RuntimeOutcome(word, outcome.Reasons, existing?.VersionNumber, null, results: call.Results);
        }

        CompiledSnapshot first = outcome.Snapshot!;
        call.Snapshot = first;
        try
        {
            var bootstrapBasis = new WriteAcceptance(WriteAcceptance.Bootstrap, noVersionExists: true);
            foreach (PlannedEffect effect in outcome.Effects)
            {
                ApplyPlannedWrite(call, effect, first, effect.Entity == "Decision" ? bootstrapBasis : null);
            }

            tx.Commit();
        }
        catch (CallFailure failure)
        {
            return new RuntimeOutcome(failure.Outcome, failure.FailureReasons, null, null, results: call.Results);
        }

        string? decisionId = call.Writes.FirstOrDefault(w => w.Entity == "decision")?.RowId;
        return new RuntimeOutcome(RuntimeOutcome.Allowed, outcome.Reasons, first.VersionNumber, first.ContentHash, decisionId, true, writes: call.Writes, payload: outcome.ToJson(), results: call.Results, factRounds: call.FactRounds);
    }

    // Saves a Configuration Draft (C46) for a person who holds a role with Can Change Configuration, directly or through
    // an AI client (G-16): OP-09 on the preview in draft mode, the draft row written by the engine's draft operation (an
    // engine-nested write with no effect, for the caller). No live row changes (document 25, section 6.1).
    public RuntimeOutcome ProposeConfigurationChange(CallerIdentity caller, ConfigurationProposal proposal)
    {
        if (proposal == null)
        {
            throw new ArgumentNullException(nameof(proposal));
        }

        return Run(caller, SnapshotPurpose.Write, call => ProposeConfigurationChangeIn(call, proposal));
    }

    // The draft operation inside a call: also used by a composed call (the MCP tool propose_configuration_change).
    private CallAnswer ProposeConfigurationChangeIn(RuntimeCall call, ConfigurationProposal proposal)
    {
        {
            JsonTreeNode parameters = RuntimeJson.Object(
                ("api", JsonTreeNode.FromString("propose-configuration-change")),
                ("changeSetJson", proposal.ChangeSet),
                ("baseVersionNumber", JsonTreeNode.FromInt64(proposal.BaseVersionNumber)),
                ("proposalRationale", JsonTreeNode.FromString(proposal.ProposalRationale)),
                ("configurationDraftId", RuntimeJson.Text(proposal.ConfigurationDraftId)),
                ("validateOnly", JsonTreeNode.FromBoolean(proposal.ValidateOnly)));
            CallAnswer? replay = ApiReplay(call, proposal.IdempotencyKey, parameters, out string? requestHash);
            if (replay != null)
            {
                return replay;
            }

            CompiledSnapshot active = call.Snapshot;
            SnapshotLoad baseLoad = Cache.LoadVersion(_configuration, active.ConfigurationSetKey, proposal.BaseVersionNumber);
            CompiledSnapshot baseVersion = baseLoad.Snapshot ?? throw CallFailure.Of(
                OperationOutcomes.InvalidInput,
                ReasonCodes.InputMalformed,
                "baseVersionNumber " + proposal.BaseVersionNumber + " is not a stored version. Start from the active version " + active.VersionNumber + ".",
                "Configuration Draft",
                null);

            ConfigurationPatch patch;
            try
            {
                patch = ConfigurationPatch.FromDocument(proposal.ChangeSet, PatchKind.Draft, "proposed change");
            }
            catch (Exception e) when (e is ArgumentException || e is ConfigurationInputException || e is FormatException || e is InvalidOperationException)
            {
                throw CallFailure.Of(OperationOutcomes.InvalidInput, ReasonCodes.InputMalformed, "changeSetJson is not a patch document: " + e.Message, "Configuration Draft", null);
            }

            var facts = new ValidationFacts(
                validatedOn: call.AsOfUtc,
                baseVersionNumber: proposal.BaseVersionNumber,
                activeVersionNumber: active.VersionNumber,
                approvalRulesVersionNumber: active.VersionNumber,
                principalKinds: PrincipalKinds(call.Tx));
            ValidationOutcome validation = _validator.Validate(new ValidationRequest(
                ContentSource.Bundle(baseVersion.ContentJson, "version " + proposal.BaseVersionNumber).WithPatch(patch),
                ValidationMode.Draft,
                ContentSource.Bundle(active.ContentJson, "version " + active.VersionNumber),
                null,
                active.ConfigurationSetKey,
                active.VersionNumber + 1,
                facts));
            call.Results["OP-09"] = RuntimeJson.Object(("outcome", JsonTreeNode.FromString(validation.Outcome)), ("payload", validation.Report.ToJson()));
            if (validation.Outcome == OperationOutcomes.InvalidInput)
            {
                ValidationIssue problem = validation.InputProblem ?? new ValidationIssue(null, ReasonCodes.InputMalformed, ReasonSeverity.Error, "The change cannot be composed.", null, null, null);
                throw new CallFailure(OperationOutcomes.InvalidInput, new[] { problem.Reason });
            }

            ValidationReport report = validation.Report;
            call.AddReasons(report.Reasons.Where(r => r.Severity != ReasonSeverity.Info));
            JsonTreeNode answer = RuntimeJson.Object(
                ("isValid", JsonTreeNode.FromBoolean(report.IsValid)),
                ("errorCount", JsonTreeNode.FromInt64(report.ErrorCount)),
                ("warningCount", JsonTreeNode.FromInt64(report.WarningCount)),
                ("changeSummary", RuntimeJson.Strings(report.ChangeSummary)));
            if (proposal.ValidateOnly)
            {
                return new CallAnswer(RuntimeOutcome.Allowed, null, false, answer);
            }

            long? expiryDays = SettingNumber(active, "draft-expiry-days");
            string person = call.Actor.OnBehalfOfPrincipalId ?? call.Actor.PrincipalId;
            JsonTreeNode after = RuntimeJson.Object(
                ("changeJson", patch.Document ?? proposal.ChangeSet),
                ("proposalRationale", JsonTreeNode.FromString(proposal.ProposalRationale)),
                ("configurationSet", JsonTreeNode.FromString(active.ConfigurationSetKey)),
                ("draftStatus", JsonTreeNode.FromString(EngineContract.DraftStatusMeanings.ToWire(DraftStatusMeaning.Draft))),
                ("baseVersionNumber", JsonTreeNode.FromInt64(proposal.BaseVersionNumber)),
                ("validationJson", report.ToJson()),
                ("changeSummary", JsonTreeNode.FromString(string.Join("\n", report.ChangeSummary))),
                ("instructedBy", JsonTreeNode.FromString(person)),
                ("expiresOn", expiryDays.HasValue ? JsonTreeNode.FromUtcDateTime(call.AsOfUtc.AddDays(expiryDays.Value)) : null));
            WriteStep step = GuardedWrite(call, new WriteSpec("configuration-draft", proposal.ConfigurationDraftId == null ? WriteOperation.Create : WriteOperation.Update)
            {
                RowId = proposal.ConfigurationDraftId,
                After = proposal.ConfigurationDraftId == null ? after : RuntimeJson.Without(after, new[] { "configurationSet", "draftStatus", "instructedBy" }),
                Nested = true,
            })!;
            JsonTreeNode payload = RuntimeJson.Merge(answer, RuntimeJson.Object(("configurationDraftId", JsonTreeNode.FromString(step.RowId))));
            if (proposal.IdempotencyKey != null)
            {
                WriteAudit(call, active, "Configuration Draft", step.RowId, "configuration-draft-saved", payload, proposal.IdempotencyKey, requestHash);
            }

            return new CallAnswer(RuntimeOutcome.Allowed, step.RowId, step.Created, payload);
        }
    }

    // The statement of a configuration change decision in plain words (product review 2026-11-24): what the draft changes
    // and the version it moves from, never the draft's ID. "Accept the configuration change: add standard change model
    // Monthly content refresh, version 1 to 2."
    private static string ConfigurationStatement(StoredRow draft, CompiledSnapshot inForce)
    {
        // Labels, not keys (wave 2 recheck QA8): "the AI use rule of Workplace / Visitor log changes from decision to never".
        string[] plain = ChangeWords.Describe(inForce.Snapshot, draft.Column("changeJson")).Select(l => l.TrimEnd('.')).ToArray();
        string[] lines = plain.Length > 0
            ? plain.Select(l => l.Length > 1 && !l.StartsWith("AI ", StringComparison.Ordinal) ? char.ToLowerInvariant(l[0]) + l.Substring(1) : l).ToArray()
            : (draft.Text("changeSummary") ?? string.Empty).Split('\n').Select(l => l.Trim().TrimEnd('.')).Where(l => l.Length > 0).ToArray();
        long from = draft.Number("baseVersionNumber") ?? 0;
        string what = lines.Length == 0 ? "a configuration change" : lines.Length == 1 ? lines[0] : lines[0] + ", and " + (lines.Length - 1) + " more change" + (lines.Length == 2 ? string.Empty : "s");
        return "Accept the configuration change: " + what + ", version " + from + " to " + (from + 1) + ".";
    }

    // The outcome key when the configuration in force holds it active for the decision type, else null.
    private static string? OutcomeIfActive(CompiledSnapshot snapshot, string decisionType, string outcome)
    {
        return snapshot.Snapshot.TryGetEntity("decision-outcome", out Core.Snapshots.SnapshotEntity? outcomes)
            && outcomes.ActiveRows.Any(r => r.KeyPath == decisionType + "/" + outcome) ? outcome : null;
    }

    // submit_record for a configuration draft (document 24, tool 18; document 25, section 6.4, step 3): the draft is
    // frozen, the engine records a configuration change decision (a gate policy change decision when the draft touches a
    // Gate Policy row) as proposed, and requests its approvals. A draft whose validation has errors still submits in this
    // build; activation runs OP-09 again and discards it.
    public RuntimeOutcome SubmitConfigurationDraft(CallerIdentity caller, string configurationDraftId)
    {
        if (string.IsNullOrEmpty(configurationDraftId))
        {
            throw new ArgumentException("The submit names its draft.", nameof(configurationDraftId));
        }

        return Run(caller, SnapshotPurpose.Write, call => SubmitConfigurationDraftIn(call, configurationDraftId));
    }

    // The submit inside a call: also used by a composed call (the MCP tool submit_record).
    private CallAnswer SubmitConfigurationDraftIn(RuntimeCall call, string configurationDraftId)
    {
        {
            StoredRow draft = call.Tx.Get("configuration-draft", configurationDraftId)
                ?? throw CallFailure.Of(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, "Configuration Draft " + configurationDraftId + " does not exist.", "Configuration Draft", configurationDraftId);
            bool touchesGatePolicy = draft.Column("changeJson") is JsonTreeNode change
                && change.TryGetMember("operations", out JsonTreeNode? operations) && operations.Kind == JsonTreeKind.Array
                && operations.Items.Any(o => RuntimeJson.Text(o, "entity") == "gate-policy");
            string decisionType = touchesGatePolicy ? "gate-policy-change" : EngineKeys.ConfigurationChangeDecisionType;

            WriteStep decision = GuardedWrite(call, new WriteSpec("decision", WriteOperation.Create)
            {
                After = RuntimeJson.Object(
                    ("decisionType", JsonTreeNode.FromString(decisionType)),
                    ("subjectKind", JsonTreeNode.FromString("configuration-draft")),
                    ("subjectConfigurationDraft", JsonTreeNode.FromString(configurationDraftId)),
                    ("statement", JsonTreeNode.FromString(ConfigurationStatement(draft, call.Snapshot))),
                    ("context", RuntimeJson.OptionalText(draft.Text("proposalRationale"))),
                    ("outcome", RuntimeJson.OptionalText(OutcomeIfActive(call.Snapshot, decisionType, "accept"))),
                    ("status", JsonTreeNode.FromString(DefaultStatus(call.Snapshot, "decision", "proposed")))),
                Nested = true,
            })!;

            GuardedWrite(call, new WriteSpec("configuration-draft", WriteOperation.Update)
            {
                RowId = configurationDraftId,
                After = RuntimeJson.Object(
                    ("draftStatus", JsonTreeNode.FromString(EngineContract.DraftStatusMeanings.ToWire(DraftStatusMeaning.Submitted))),
                    ("submittedOn", JsonTreeNode.FromUtcDateTime(call.AsOfUtc)),
                    ("changeDecision", JsonTreeNode.FromString(decision.RowId))),
                Nested = true,
            });

            JsonTreeNode requested = RequestApprovalsIn(call, decision.RowId);
            JsonTreeNode payload = RuntimeJson.Merge(requested, RuntimeJson.Object(("status", JsonTreeNode.FromString("submitted")), ("decisionId", JsonTreeNode.FromString(decision.RowId))));
            return new CallAnswer(RuntimeOutcome.Allowed, decision.RowId, true, payload);
        }
    }

    // The activate-configuration effect of an accepted configuration change decision (document 25, section 6.4, step 5).
    // Activation is not callable: only this effect starts it. Its writes pass OP-08 as engine-nested writes of the effect,
    // inside the transaction of the write that accepted the decision. After the commit, the version probe of the next call
    // anywhere sees the new version number (step 6).
    private void Activate(RuntimeCall call, string draftId, string decisionId)
    {
        StoredRow draft = call.Tx.Get("configuration-draft", draftId)
            ?? throw CallFailure.Of(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, "Configuration Draft " + draftId + " does not exist.", "Configuration Draft", draftId);
        StoredRow decision = call.Tx.Get("decision", decisionId)
            ?? throw CallFailure.Of(RuntimeOutcome.NotFound, ReasonCodes.RefNotFound, "Decision " + decisionId + " does not exist.", "Decision", decisionId);

        if (!EngineContract.DraftStatusMeanings.TryParse(draft.Text("draftStatus"), out DraftStatusMeaning draftStatus)
            || !EngineContract.DecisionStatusMeanings.TryParse(decision.Text("statusMeaning"), out DecisionStatusMeaning decisionStatus))
        {
            throw CallFailure.Of(OperationOutcomes.InvalidInput, ReasonCodes.InputRequired, "The draft's status or the decision's status meaning is missing, so activation cannot judge them.", "Configuration Draft", draftId);
        }

        CompiledSnapshot active = call.Snapshot;
        int baseNumber = (int)(draft.Number("baseVersionNumber") ?? active.VersionNumber);
        SnapshotLoad baseLoad = Cache.LoadVersion(_configuration, active.ConfigurationSetKey, baseNumber);
        CompiledSnapshot baseVersion = baseLoad.Snapshot ?? throw new CallFailure(OperationOutcomes.Unavailable, new[] { baseLoad.Problem! });

        JsonTreeNode changeJson = WithModelApprovals(
            draft.Column("changeJson") ?? throw CallFailure.Of(OperationOutcomes.InvalidInput, ReasonCodes.InputRequired, "Configuration Draft " + draftId + " holds no change.", "Configuration Draft", draftId),
            decisionId);
        var request = new ActivationRequest(
            draftId,
            draftStatus,
            decisionId,
            decisionStatus,
            ContentSource.Bundle(baseVersion.ContentJson, "version " + baseNumber),
            baseNumber,
            ContentSource.Bundle(active.ContentJson, "version " + active.VersionNumber),
            active.VersionNumber,
            ConfigurationPatch.FromDocument(changeJson, PatchKind.Draft, "Configuration Draft " + draftId),
            resultingVersionNumber: (int?)draft.Number("resultingVersion"),
            approvalRulesVersionNumber: (int?)decision.Number("configurationSetVersion"),
            principalKinds: PrincipalKinds(call.Tx),
            instructedByPrincipalId: draft.Text("instructedBy"),
            configurationSetKey: active.ConfigurationSetKey);

        ActivationOutcome outcome = _activator.Activate(call.Context(active, nested: true), request);
        call.Results["activation"] = RuntimeJson.With(outcome.ToJson(), "outcome", JsonTreeNode.FromString(outcome.Outcome));
        call.AddReasons(outcome.Reasons.Where(r => r.Severity != ReasonSeverity.Error || outcome.Outcome == ActivationOutcome.Discarded).Select(r => r.Severity == ReasonSeverity.Error ? new Reason(r.Code, ReasonSeverity.Warning, r.Message, r.Entity, r.Ref) : r));
        switch (outcome.Outcome)
        {
            case ActivationOutcome.Activated:
            case ActivationOutcome.Discarded:
                foreach (PlannedEffect effect in outcome.Effects)
                {
                    ApplyPlannedWrite(call, effect, active, null);
                }

                break;
            case ActivationOutcome.AlreadyActivated:
                break;
            default:
                throw new CallFailure(outcome.Outcome == ActivationOutcome.InvalidInput ? OperationOutcomes.InvalidInput : OperationOutcomes.Refused, outcome.Reasons);
        }
    }

    // A standard change model counts only once a person approved it (C21 Approved By Decision, a working set field; OP-12).
    // The accepted configuration change decision that lists or changes a model is that approval (document 21, B21-9;
    // document 25, B25-14), so activation names it on every model the draft upserts without naming one itself. A changed
    // model is approved again by the decision that changed it. The draft's own Change Json stays as the admin wrote it.
    private static JsonTreeNode WithModelApprovals(JsonTreeNode changeJson, string decisionId)
    {
        if (changeJson.Kind != JsonTreeKind.Object || !changeJson.TryGetMember("operations", out JsonTreeNode? operations) || operations.Kind != JsonTreeKind.Array)
        {
            return changeJson;
        }

        bool changed = false;
        var stamped = new List<JsonTreeNode>();
        foreach (JsonTreeNode operation in operations.Items)
        {
            bool model = RuntimeJson.Text(operation, "op") == "upsert" && RuntimeJson.Text(operation, "entity") == "standard-change-model"
                && operation.TryGetMember("values", out JsonTreeNode? values) && values.Kind == JsonTreeKind.Object && !values.TryGetMember("approvedByDecisionId", out _);
            if (model)
            {
                operation.TryGetMember("values", out JsonTreeNode? before);
                stamped.Add(RuntimeJson.With(operation, "values", RuntimeJson.With(before!, "approvedByDecisionId", JsonTreeNode.FromString(decisionId))));
                changed = true;
            }
            else
            {
                stamped.Add(operation);
            }
        }

        return changed ? RuntimeJson.With(changeJson, "operations", JsonTreeNode.CreateArray(stamped)) : changeJson;
    }

    // <prefix>_GetEngineInfo: the engine version, the snapshot schema version and the hash of the version in force.
    public RuntimeOutcome GetEngineInfo(CallerIdentity caller)
    {
        return Run(caller, SnapshotPurpose.Read, call => new CallAnswer(
            "computed",
            null,
            false,
            RuntimeJson.Object(
                ("engineVersion", JsonTreeNode.FromString(EngineInfo.Version)),
                ("snapshotSchemaVersion", JsonTreeNode.FromString(call.Snapshot.Snapshot.SchemaVersion.Major + "." + call.Snapshot.Snapshot.SchemaVersion.Minor)),
                ("snapshotHash", JsonTreeNode.FromString(call.Snapshot.ContentHash)),
                ("configurationVersion", JsonTreeNode.FromInt64(call.Snapshot.VersionNumber)))));
    }

    // Any read operation whose input the caller sends whole, facts included (OP-05, OP-10 and OP-11 take no store facts
    // beyond what the caller names). The snapshot in force and the caller's roles still come from the runtime.
    public RuntimeOutcome Evaluate(CallerIdentity caller, EngineOperation operation, JsonTreeNode input)
    {
        if (OperationCatalog.Get(operation).IsWrite)
        {
            throw new ArgumentException("A write goes through Write, never Evaluate.", nameof(operation));
        }

        return Run(caller, SnapshotPurpose.Read, call =>
        {
            EvaluationResult<JsonTreeNode?> result = Evaluate(call, operation, OperationJson.ReadsSnapshot(operation) ? call.Snapshot : null, input);
            call.AddReasons(result.Reasons);
            return new CallAnswer(result.Outcome, null, false, result.Payload);
        });
    }
}
