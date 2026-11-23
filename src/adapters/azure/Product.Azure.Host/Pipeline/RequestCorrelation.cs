using System;
using System.Globalization;
using System.Threading;
using Microsoft.AspNetCore.Http;

namespace Product.Azure.Host;

/// <summary>
/// The source of request correlation IDs. One value per HTTP request: the same value goes to the MCP envelope, the Audit
/// Entry, telemetry, the <c>X-Correlation-Id</c> response header and the Azure SQL session context.
/// </summary>
public interface IRequestCorrelation
{
    /// <summary>A new correlation ID.</summary>
    string Next();
}

/// <summary>The default correlation source, and a repeatable one for tests.</summary>
public sealed class RequestCorrelation : IRequestCorrelation
{
    private readonly Func<string> _next;

    /// <summary>The default source: <c>req-</c> and a random GUID in the N format.</summary>
    public RequestCorrelation()
        : this(() => "req-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture))
    {
    }

    private RequestCorrelation(Func<string> next)
    {
        _next = next;
    }

    /// <inheritdoc />
    public string Next()
    {
        return _next();
    }

    /// <summary>
    /// A repeatable source for tests: the prefix and a running number in six digits (<c>mcp-000001</c>), the same values
    /// as the MCP core's own sequential source.
    /// </summary>
    public static IRequestCorrelation Sequential(string prefix = "mcp-")
    {
        long sequence = 0;
        return new RequestCorrelation(() => prefix + Interlocked.Increment(ref sequence).ToString("D6", CultureInfo.InvariantCulture));
    }
}

/// <summary>What the product pipeline knows about the current HTTP request.</summary>
public static class ProductRequest
{
    /// <summary>The response header that carries the request's correlation ID.</summary>
    public const string CorrelationHeader = "X-Correlation-Id";

    private const string CorrelationKey = "Product.CorrelationId";
    private const string CallerKey = "Product.Caller";
    private const string ContextKey = "Product.CallContext";

    /// <summary>The request's correlation ID. Made on first use when the caller-context middleware did not run.</summary>
    public static string CorrelationId(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.Items.TryGetValue(CorrelationKey, out object? value) && value is string id)
        {
            return id;
        }

        string made = ((IRequestCorrelation?)context.RequestServices.GetService(typeof(IRequestCorrelation)) ?? new RequestCorrelation()).Next();
        context.Items[CorrelationKey] = made;
        return made;
    }

    /// <summary>The mapped caller of the request, or null when it is not signed in.</summary>
    public static CallerClaims? Caller(HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.Items.TryGetValue(CallerKey, out object? value))
        {
            return value as CallerClaims;
        }

        var mapper = (ICallerMapper?)context.RequestServices.GetService(typeof(ICallerMapper));
        CallerClaims? caller = mapper?.Map(context.User);
        context.Items[CallerKey] = caller;
        return caller;
    }

    /// <summary>The store call context the middleware entered for this request, or null.</summary>
    public static Product.Azure.Stores.StoreCallContext? CallContext(HttpContext context)
    {
        return context != null && context.Items.TryGetValue(ContextKey, out object? value) ? value as Product.Azure.Stores.StoreCallContext : null;
    }

    internal static void SetCorrelationId(HttpContext context, string correlationId)
    {
        context.Items[CorrelationKey] = correlationId;
    }

    internal static void SetCaller(HttpContext context, CallerClaims? caller)
    {
        context.Items[CallerKey] = caller;
    }

    internal static void SetCallContext(HttpContext context, Product.Azure.Stores.StoreCallContext callContext)
    {
        context.Items[ContextKey] = callContext;
    }
}
