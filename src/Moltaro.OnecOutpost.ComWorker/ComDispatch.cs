using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.ComWorker;

// Late-bound calls to the objects of 1C through IDispatch, the only way the worker reaches them. The connection and the
// objects it gives come without type information, so a member is reached by its name. A call that 1C refuses throws
// the COMException that carries the text of 1C directly.
[SupportedOSPlatform("windows")]
internal static class ComDispatch
{
    public static object? Get(object target, string name)
    {
        return Call(target, name, BindingFlags.GetProperty, []);
    }

    public static void Set(object target, string name, object? value)
    {
        Call(target, name, BindingFlags.SetProperty, [value]);
    }

    // A null argument goes as VT_EMPTY, which 1C reads as Undefined.
    public static object? Invoke(object target, string name, params object?[] arguments)
    {
        return Call(target, name, BindingFlags.InvokeMethod, arguments);
    }

    // Drops the reference that one call gave the worker. The owner of a COM object calls it once for each time it
    // obtained the object.
    public static void Release(object target)
    {
        Marshal.ReleaseComObject(target);
    }

    private static object? Call(object target, string name, BindingFlags flags, object?[] arguments)
    {
        try
        {
            return target.GetType().InvokeMember(name, flags, null, target, arguments, CultureInfo.InvariantCulture);
        }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(failure.InnerException).Throw();

            throw;
        }
    }
}
