using Brutal.Authoring.Editor.App;
using Xunit;

namespace Brutal.Authoring.Tests;

public sealed class ShellCloseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void No_callback_keeps_the_existing_immediate_OS_decision(bool requested)
    {
        var clears = 0;
        Assert.Equal(requested, ShellClose.Decide(requested, () => clears++));
        Assert.Equal(0, clears);
    }

    [Fact]
    public void Clean_host_accepts_after_native_request_is_cleared()
    {
        var native = true; var calls = 0;
        Assert.True(ShellClose.Decide(native, () => native = false, request =>
        {
            calls++; Assert.True(request); Assert.False(native); return true;
        }));
        Assert.Equal(1, calls);
        Assert.False(native);
    }

    [Fact]
    public void Cancel_keeps_loop_running_and_does_not_replay_the_native_request()
    {
        var native = true; var requests = new List<bool>();
        bool Cancel(bool request) { requests.Add(request); return false; }
        Assert.False(ShellClose.Decide(native, () => native = false, Cancel));
        Assert.False(ShellClose.Decide(native, () => native = false, Cancel));
        Assert.Equal(new[] { true, false }, requests);
    }

    [Fact]
    public void Pending_save_failure_does_not_close_and_later_explicit_acceptance_does()
    {
        var native = true; var accepted = false; var calls = 0;
        bool Decision(bool request) { calls++; return accepted; }
        Assert.False(ShellClose.Decide(native, () => native = false, Decision));
        // The host has not accepted after a failed save: the shell must not reinterpret this as permission.
        Assert.False(ShellClose.Decide(native, () => native = false, Decision));
        accepted = true;
        Assert.True(ShellClose.Decide(native, () => native = false, Decision));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void A_throwing_decision_never_returns_an_acceptance_and_clears_the_request_first()
    {
        var native = true;
        Assert.Throws<InvalidOperationException>(() => ShellClose.Decide(native, () => native = false,
            _ => throw new InvalidOperationException("Synthetic decision failure")));
        Assert.False(native);
    }
}
