using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Product.Engine.Core;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Mcp.Core.Envelope;
using Product.Mcp.Core.Json;

namespace Product.Azure.Host;

// The response envelope of the REST routes (document 24, section 3.4): the same shape the MCP tools return, so the web
// app and a pipeline read one format. Built here for the routes that call the engine directly.
internal static class Envelope
{
    public static JsonTreeNode Success(
        JsonTreeNode data,
        string summary,
        DateTime asOfUtc,
        int configurationSetVersion,
        string correlationId,
        string? recordUrl = null,
        IEnumerable<Reason>? reasons = null,
        IEnumerable<(string Message, string? Code, string? Ref)>? extraWarnings = null)
    {
        var warnings = (reasons ?? Enumerable.Empty<Reason>())
            .Where(r => r.Severity == ReasonSeverity.Warning)
            .Select(r => (r.Message, (string?)r.Code, r.Ref))
            .Concat(extraWarnings ?? Enumerable.Empty<(string, string?, string?)>())
            .Select(w => Js.Obj(
                ("message", JsonTreeNode.FromString(Clip(string.IsNullOrWhiteSpace(w.Item1) ? "Note." : w.Item1, 1000))),
                ("code", w.Item2 != null && Reason.IsWellFormedCode(w.Item2) ? JsonTreeNode.FromString(w.Item2) : null),
                ("ref", w.Item3 == null ? null : JsonTreeNode.FromString(Clip(w.Item3, 200)))));
        return Js.Obj(
            ("ok", JsonTreeNode.True),
            ("summary", JsonTreeNode.FromString(Clip(summary, 1000))),
            ("data", data),
            ("links", recordUrl == null ? null : Js.Obj(("recordUrl", JsonTreeNode.FromString(recordUrl)))),
            ("asOf", Js.Utc(asOfUtc)),
            ("configurationSetVersion", JsonTreeNode.FromInt64(Math.Max(1, configurationSetVersion))),
            ("couldNotRead", Js.EmptyArray),
            ("warnings", Js.Arr(warnings)),
            ("untrustedFields", Js.EmptyArray),
            ("nextSteps", Js.EmptyArray),
            ("correlationId", JsonTreeNode.FromString(correlationId)));
    }

    public static JsonTreeNode Failure(McpError error, string correlationId, DateTime? asOfUtc = null, int? configurationSetVersion = null, string? summary = null)
    {
        return Js.Obj(
            ("ok", JsonTreeNode.False),
            ("summary", JsonTreeNode.FromString(Clip(string.IsNullOrWhiteSpace(summary) ? error.Message : summary!, 1000))),
            ("error", error.ToJson()),
            ("asOf", asOfUtc.HasValue ? Js.Utc(asOfUtc.Value) : null),
            ("configurationSetVersion", configurationSetVersion.HasValue && configurationSetVersion.Value >= 1 ? JsonTreeNode.FromInt64(configurationSetVersion.Value) : null),
            ("correlationId", JsonTreeNode.FromString(correlationId)));
    }

    private static string Clip(string text, int max)
    {
        return text.Length <= max ? text : text.Substring(0, max - 3) + "...";
    }
}

// Reading and writing JSON over HTTP for the product's routes.
internal static class HttpJson
{
    public const string JsonContentType = "application/json; charset=utf-8";

    public static async Task Write(HttpContext context, int status, JsonTreeNode body, IReadOnlyDictionary<string, string>? headers = null)
    {
        context.Response.StatusCode = status;
        if (headers != null)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                context.Response.Headers[header.Key] = header.Value;
            }
        }

        context.Response.ContentType = JsonContentType;
        await context.Response.WriteAsync(Js.Write(body), Encoding.UTF8, context.RequestAborted).ConfigureAwait(false);
    }

    public static async Task WriteText(HttpContext context, int status, string? json, IReadOnlyDictionary<string, string>? headers = null)
    {
        context.Response.StatusCode = status;
        if (headers != null)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                context.Response.Headers[header.Key] = header.Value;
            }
        }

        if (json == null)
        {
            return;
        }

        context.Response.ContentType = JsonContentType;
        await context.Response.WriteAsync(json, Encoding.UTF8, context.RequestAborted).ConfigureAwait(false);
    }

    public static Task WriteFailure(HttpContext context, McpError error, Product.Mcp.Core.Contracts.ContractSet contracts, DateTime? asOfUtc = null, int? version = null, string? summary = null)
    {
        int status = ProductErrorMap.HttpStatus(error.Code, contracts);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (error.RetryAfterSeconds.HasValue)
        {
            headers["Retry-After"] = error.RetryAfterSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (status == 401)
        {
            headers["WWW-Authenticate"] = "Bearer";
        }

        return Write(context, status, Envelope.Failure(error, ProductRequest.CorrelationId(context), asOfUtc, version, summary), headers);
    }

    // True for application/json with or without parameters, and for a +json media type.
    public static bool IsJson(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        string media = contentType!.Split(';')[0].Trim();
        return media.Equals("application/json", StringComparison.OrdinalIgnoreCase) || media.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    // Reads the body as UTF-8 text up to the limit. Null when it is larger.
    public static async Task<string?> ReadBody(HttpContext context, int maxBytes)
    {
        long? declared = context.Request.ContentLength;
        if (declared.HasValue && declared.Value > maxBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16384];
        while (true)
        {
            int read = await context.Request.Body.ReadAsync(chunk.AsMemory(0, chunk.Length), context.RequestAborted).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return new UTF8Encoding(false, false).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
