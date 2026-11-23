using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Product.Azure.Stores;

namespace Product.Azure.Tests;

// Stands between the host and its store for the health tests: every call goes to the real store, except the readiness
// check, which a test can make refuse or hang. Counts the readiness checks that reached it.
internal sealed class StoreProbe
{
    private readonly ManualResetEventSlim _released = new(true);
    private int _checks;

    // False makes the readiness check answer that the store cannot be reached.
    public bool Reachable { get; set; } = true;

    public int Checks => Volatile.Read(ref _checks);

    // Makes every readiness check wait until Release is called.
    public void Hang()
    {
        _released.Reset();
    }

    public void Release()
    {
        _released.Set();
    }

    // Wraps the store the host registered last, so the host and the tests see one store through the probe.
    public void Install(IServiceCollection services)
    {
        ServiceDescriptor inner = services.Last(d => d.ServiceType == typeof(IProductStore));
        Func<IServiceProvider, object> factory = inner.ImplementationFactory
            ?? throw new InvalidOperationException("The host registers its store with a factory.");
        services.AddSingleton<IProductStore>(sp => Wrap((IProductStore)factory(sp)));
    }

    public IProductStore Wrap(IProductStore store)
    {
        IProductStore proxy = DispatchProxy.Create<IProductStore, Forwarder>();
        var forwarder = (Forwarder)(object)proxy;
        forwarder.Inner = store;
        forwarder.Probe = this;
        return proxy;
    }

    internal bool CanConnect(IProductStore inner)
    {
        Interlocked.Increment(ref _checks);
        _released.Wait(TimeSpan.FromSeconds(30));
        return Reachable && inner.CanConnect();
    }

    // Public with a parameterless constructor, as DispatchProxy needs.
    public class Forwarder : DispatchProxy
    {
        internal IProductStore? Inner { get; set; }

        internal StoreProbe? Probe { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null || Inner == null || Probe == null)
            {
                throw new InvalidOperationException("The probe is not set up.");
            }

            if (targetMethod.Name == nameof(IProductStore.CanConnect) && targetMethod.DeclaringType == typeof(IProductStore))
            {
                return Probe.CanConnect(Inner);
            }

            try
            {
                return targetMethod.Invoke(Inner, args);
            }
            catch (TargetInvocationException wrapped) when (wrapped.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(wrapped.InnerException).Throw();
                throw;
            }
        }
    }
}
