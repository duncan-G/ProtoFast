using ProtoFast.Auth.Api.Accounts;
using Xunit;

namespace ProtoFast.Auth.IntegrationTests;

public class EmailChangeStoreTests
{
    [Fact]
    public async Task Cancelling_does_not_let_the_same_mailbox_be_mailed_again()
    {
        var store = new StubEmailChangeStore();
        var cooldown = EmailChangeCode.RequestCooldown;
        var ct = TestContext.Current.CancellationToken;

        Assert.True(await store.TryTakeSendSlotAsync("realm", "sub", "a@x.test", cooldown, ct));
        await store.DeleteAsync("realm", "sub", ct);

        Assert.False(await store.TryTakeSendSlotAsync("realm", "sub", "a@x.test", cooldown, ct));
    }

    [Fact]
    public async Task A_different_mailbox_after_cancel_is_not_waiting_on_the_typo()
    {
        var store = new StubEmailChangeStore();
        var cooldown = EmailChangeCode.RequestCooldown;
        var ct = TestContext.Current.CancellationToken;

        Assert.True(await store.TryTakeSendSlotAsync("realm", "sub", "typo@x.test", cooldown, ct));
        await store.DeleteAsync("realm", "sub", ct);

        Assert.True(await store.TryTakeSendSlotAsync("realm", "sub", "ok@x.test", cooldown, ct));
    }

    [Fact]
    public async Task Cycling_addresses_still_hits_the_account_window()
    {
        var store = new StubEmailChangeStore();
        var cooldown = EmailChangeCode.RequestCooldown;
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < EmailChangeCode.MaxSendsPerWindow; i++)
        {
            Assert.True(await store.TryTakeSendSlotAsync(
                "realm", "sub", $"n{i}@x.test", cooldown, ct));
            await store.DeleteAsync("realm", "sub", ct);
        }

        Assert.False(await store.TryTakeSendSlotAsync(
            "realm", "sub", "overflow@x.test", cooldown, ct));
    }

    [Fact]
    public async Task A_send_that_never_left_gives_the_slot_back()
    {
        var store = new StubEmailChangeStore();
        var cooldown = EmailChangeCode.RequestCooldown;
        var ct = TestContext.Current.CancellationToken;

        Assert.True(await store.TryTakeSendSlotAsync("realm", "sub", "a@x.test", cooldown, ct));
        await store.ReleaseSendSlotAsync("realm", "sub", "a@x.test", ct);

        Assert.True(await store.TryTakeSendSlotAsync("realm", "sub", "a@x.test", cooldown, ct));
    }
}
