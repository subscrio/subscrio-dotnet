using System.Text.Json;
using System.Text.Json.Nodes;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Infrastructure.Database;
namespace Subscrio.Core.Application.Hooks;

public sealed class AccountingMutationHookEvent
{
    public required string Type
    {
        get; init;
    }
    public required string Phase
    {
        get; init;
    }
    public string Source { get; init; } = "api";
    public string OccurredAt { get; init; } = DateTime.UtcNow.ToString("O");
    public required JsonObject Input
    {
        get; init;
    }
    public JsonElement? Result
    {
        get; init;
    }
}
public delegate Task AccountingHookHandler(AccountingMutationHookEvent evt, CancellationToken cancellationToken);
public sealed class CommittedOperationHookException(object result, Exception inner) : Exception("The operation committed, but its after-hook failed. Retry with the same idempotency key to retrieve the committed result.", inner)
{
    public object Result { get; } = result;
}
internal sealed class TransactionHooks(HookDispatcher hooks)
{
    internal async Task<T> Before<T>(string mutation, T input, params string[] permitted)
    {
        var original = JsonSerializer.SerializeToNode(input, DatabaseSession.JsonOptions)!.AsObject();
        var evt = new AccountingMutationHookEvent { Type = mutation + ".before", Phase = "before", Input = (JsonObject)original.DeepClone() };
        await hooks.EmitAccountingAsync(evt);
        foreach (var k in original.Select(x => x.Key).Concat(evt.Input.Select(x => x.Key)).Distinct())
            if (!permitted.Contains(k) && !JsonNode.DeepEquals(original[k], evt.Input[k]))
                throw new ValidationException("Hook cannot change " + k);
        return evt.Input.Deserialize<T>(DatabaseSession.JsonOptions)!;
    }
    internal Task After(string mutation, object input, object result) => DatabaseSession.AfterCommit(async () => { try { await hooks.EmitAccountingAsync(new() { Type = mutation + ".after", Phase = "after", Input = JsonSerializer.SerializeToNode(input, DatabaseSession.JsonOptions)!.AsObject(), Result = JsonSerializer.SerializeToElement(result, DatabaseSession.JsonOptions) }); } catch (Exception ex) { throw new CommittedOperationHookException(result, ex); } });
}
