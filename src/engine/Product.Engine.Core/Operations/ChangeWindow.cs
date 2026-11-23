using System;
using Product.Engine.Core.Canonical;
using Product.Engine.Core.Contract;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Core.Operations;

// OP-05 input (document 25, section 5.7). Dates only: the operation reads no configuration.
public sealed class WindowRequest
{
    public WindowRequest(DateTime? windowStartUtc, DateTime? windowEndUtc, DateTime eventUtc)
    {
        WindowStartUtc = Utc(windowStartUtc, nameof(windowStartUtc));
        WindowEndUtc = Utc(windowEndUtc, nameof(windowEndUtc));
        EventUtc = Utc(eventUtc, nameof(eventUtc))!.Value;
    }

    // When the change may start (inclusive). Null when the window has no start.
    public DateTime? WindowStartUtc { get; }

    // When the change must be done (exclusive). Null when the window has no end.
    public DateTime? WindowEndUtc { get; }

    // The moment to test.
    public DateTime EventUtc { get; }

    public static WindowRequest FromJson(JsonTreeNode input)
    {
        var wire = new WireObject(input, "input", "windowStartUtc", "windowEndUtc", "eventUtc");
        return new WindowRequest(wire.OptionalUtc("windowStartUtc"), wire.OptionalUtc("windowEndUtc"), wire.RequiredUtc("eventUtc"));
    }

    private static DateTime? Utc(DateTime? value, string name)
    {
        if (value.HasValue && value.Value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Every moment is a UTC date-time (DateTimeKind.Utc).", name);
        }

        return value;
    }
}

// OP-05 output.
public sealed class WindowResult : IOperationPayload
{
    public WindowResult(WindowState state, int minutesOutside)
    {
        State = state;
        MinutesOutside = minutesOutside;
    }

    // inside, before, after or no-window.
    public WindowState State { get; }

    // How far outside, in whole minutes rounded up. 0 when inside or when there is no window.
    public int MinutesOutside { get; }

    public bool IsOutside => State == WindowState.Before || State == WindowState.After;

    public JsonTreeNode ToJson()
    {
        return new WireWriter()
            .Add("state", EngineContract.WindowStates.ToWire(State))
            .Add("minutesOutside", MinutesOutside)
            .Build();
    }
}

// OP-05 Check change window (document 25, section 5.7). The start is inclusive, the end exclusive, all in UTC.
// Time zones are a display concern. Outcomes: inside, outside, no-window. Enforcing a window stays in the
// deployment tool (D-13): this operation only says where the moment falls.
public static class ChangeWindowOperation
{
    public static EvaluationResult<WindowResult?> Evaluate(EvaluationContext context, WindowRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return OperationShell.Run(context, null, needsSnapshot: false, () => Answer(request));
    }

    public static EvaluationResult<WindowResult?> Evaluate(EvaluationContext context, JsonTreeNode input)
    {
        return OperationShell.Run(context, null, needsSnapshot: false, () => Answer(WindowRequest.FromJson(input)));
    }

    // The pure rule, shared with OP-04.
    public static WindowResult Check(DateTime? windowStartUtc, DateTime? windowEndUtc, DateTime eventUtc)
    {
        if (windowStartUtc == null && windowEndUtc == null)
        {
            return new WindowResult(WindowState.NoWindow, 0);
        }

        if (windowStartUtc.HasValue && eventUtc < windowStartUtc.Value)
        {
            return new WindowResult(WindowState.Before, MinutesUp(windowStartUtc.Value - eventUtc));
        }

        if (windowEndUtc.HasValue && eventUtc >= windowEndUtc.Value)
        {
            return new WindowResult(WindowState.After, MinutesUp(eventUtc - windowEndUtc.Value));
        }

        return new WindowResult(WindowState.Inside, 0);
    }

    // A window that ends before or when it starts holds no moment at all, so it is malformed input.
    internal static void CheckWindow(DateTime? windowStartUtc, DateTime? windowEndUtc, string where)
    {
        if (windowStartUtc.HasValue && windowEndUtc.HasValue && windowEndUtc.Value <= windowStartUtc.Value)
        {
            throw OperationStop.Input(ReasonCodes.InputMalformed, where + " ends before or when it starts, so it holds no moment.");
        }
    }

    private static OperationAnswer<WindowResult> Answer(WindowRequest request)
    {
        CheckWindow(request.WindowStartUtc, request.WindowEndUtc, "The change window");
        WindowResult result = Check(request.WindowStartUtc, request.WindowEndUtc, request.EventUtc);
        switch (result.State)
        {
            case WindowState.NoWindow:
                return new OperationAnswer<WindowResult>("no-window", result, new[] { ReasonCatalog.Create(ReasonCodes.WindowNone) });
            case WindowState.Inside:
                return new OperationAnswer<WindowResult>("inside", result);
            default:
                return new OperationAnswer<WindowResult>("outside", result, new[] { ReasonCatalog.Create(ReasonCodes.WindowOutside) });
        }
    }

    // Whole minutes, rounded up, and at least 1 once the moment is outside: a second late is still late.
    private static int MinutesUp(TimeSpan distance)
    {
        double minutes = Math.Ceiling(distance.TotalMinutes);
        if (minutes < 1)
        {
            return 1;
        }

        return minutes > int.MaxValue ? int.MaxValue : (int)minutes;
    }
}
