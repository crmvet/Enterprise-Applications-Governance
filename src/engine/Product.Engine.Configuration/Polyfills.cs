// Compiler attributes that net48 and netstandard2.0 lack.
// The compiler finds them by name, so internal copies are enough. Nothing here runs at run time.
#if NETFRAMEWORK || NETSTANDARD2_0

namespace System.Diagnostics.CodeAnalysis
{
    // Says an out argument is not null when the method returns the given value.
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        public NotNullWhenAttribute(bool returnValue)
        {
            ReturnValue = returnValue;
        }

        public bool ReturnValue { get; }
    }
}

#endif
