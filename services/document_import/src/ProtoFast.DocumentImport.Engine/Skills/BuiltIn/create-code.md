---
name: create-code
description: Add or replace a C# script in one of your skills; run it with execute_code.
---
Use code for anything deterministic: counting, splitting, reshaping JSON, merging documents.
It is cheaper and more reliable than doing it yourself, and it is what stages are distilled into.

Script `create`. Args:

```json
{ "skill": "screenplay-outline", "script": "split-scenes", "description": "args and result in one line", "source": "C#" }
```

The skill must exist (`create-skill`). The source is one C# file:

```csharp
public static class Script
{
    public static async Task<object?> RunAsync(ScriptContext context)
    {
        var text = await context.ReadTextAsync(context.Arg<ArtifactRef>("artifact"));
        return new { lines = text.Split('\n').Length };
    }
}
```

- `context.Arg<T>(name)` reads one property of the `args` passed to execute_code (camelCase JSON);
  `context.Args` is the whole JsonElement.
- `context.ReadTextAsync(ArtifactRef)` reads an artifact.
- `context.RunAsync(skill, script, args)` runs any other script, built-in ones included, and returns
  its result as a JsonElement; it throws if that script fails. For example
  `await context.RunAsync("write-artifact", "write-artifact", new { stageId, contract, content, inputs })`
  records a stage without its content passing through you.
- The return value is serialized to JSON (camelCase) and is the script's result.

Usings for System, collections, LINQ, text, regex, JSON and tasks are already in scope. Scripts
may only use those: no files, network, processes, environment, reflection, threads or `unsafe`.
A script that does not compile is rejected with the compiler's errors. Scripts time out.

Returns `{ "skill": { "id": "...", "version": 2 }, "script": "...", "hash": "..." }`.
