using System.Reflection;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Minimal recording test double for any interface, built on <see cref="DispatchProxy"/>.
/// Every call is recorded (method name + arguments). Behaviour is configured per method name
/// with <see cref="On"/>; unconfigured methods return a neutral default
/// (<c>Task.CompletedTask</c>, <c>Task.FromResult(default)</c>, <c>default</c>).
/// </summary>
public class Stub : DispatchProxy
{
    private readonly Dictionary<string, Func<object?[], object?>> _handlers = new();
    private readonly List<(string Method, object?[] Args)> _calls = [];
    private readonly Lock _lock = new();

    public static T Create<T>(out Stub stub) where T : class
    {
        var proxy = Create<T, Stub>();
        stub = (Stub)(object)proxy;
        return proxy;
    }

    public static T Create<T>(Action<Stub>? configure = null) where T : class
    {
        var proxy = Create<T>(out var stub);
        configure?.Invoke(stub);
        return proxy;
    }

    public Stub On(string methodName, Func<object?[], object?> handler)
    {
        _handlers[methodName] = handler;
        return this;
    }

    public IReadOnlyList<(string Method, object?[] Args)> Calls
    {
        get { lock (_lock) return _calls.ToList(); }
    }

    public IReadOnlyList<object?[]> CallsTo(string methodName) =>
        Calls.Where(c => c.Method == methodName).Select(c => c.Args).ToList();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        args ??= [];
        lock (_lock) _calls.Add((targetMethod.Name, args));

        return _handlers.TryGetValue(targetMethod.Name, out var handler)
            ? handler(args)
            : DefaultFor(targetMethod.ReturnType);
    }

    private static object? DefaultFor(Type type)
    {
        if (type == typeof(void)) return null;
        if (type == typeof(Task)) return Task.CompletedTask;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var inner = type.GetGenericArguments()[0];
            var value = inner.IsValueType ? Activator.CreateInstance(inner) : null;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [value]);
        }
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
