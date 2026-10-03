using System.Reflection;
using System.Runtime.ExceptionServices;

namespace ProtoFast.DocumentImport.Engine.Skills;

internal sealed class CompiledScript(MethodInfo entry)
{
    public async Task<object?> InvokeAsync(ScriptContext context)
    {
        object? result;
        try
        {
            result = entry.Invoke(null, [context]);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }

        if (result is not Task task)
        {
            return result;
        }

        await task;

        // The declared type, because an async Task method's runtime type is Task<VoidTaskResult>.
        return entry.ReturnType.IsGenericType
            ? entry.ReturnType.GetProperty(nameof(Task<object>.Result))!.GetValue(task)
            : null;
    }
}
