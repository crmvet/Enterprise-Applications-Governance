using Product.Engine.Runtime;
using Product.Mcp.Core.Backend.InProcess;
using Product.Mcp.Core.Contracts;
using Product.Mcp.Core.Envelope;

namespace Product.Azure.Host;

// Maps a failed engine answer to one of the 11 fixed error codes, for the routes that call the engine directly (the
// record endpoint and the approval outcome endpoint). It is the MCP server core's own map (EngineErrorMap), so every
// entry point answers the same refusal with the same code: the engine reasons errors.json names for a code, then the
// reason's catalog category, then the runtime outcome word. A refusal by an AI rule is ai_cannot_do_this only for an AI
// caller.
internal static class ProductErrorMap
{
    public static McpError Map(RuntimeOutcome outcome, ContractSet contracts, bool aiCaller)
    {
        return EngineErrorMap.Map(outcome, contracts, aiCaller);
    }

    // The HTTP status of a fixed code, from errors.json.
    public static int HttpStatus(string code, ContractSet contracts)
    {
        return contracts.Errors.TryGetValue(code, out ErrorCodeInfo? info) ? info.HttpStatus : 500;
    }
}
