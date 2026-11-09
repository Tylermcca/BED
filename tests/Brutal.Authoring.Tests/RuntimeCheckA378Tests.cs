using Brutal.Authoring.Launcher;
using Xunit;

// Injected delegates only: these prepared tests do not load a native DLL or show a window.
public sealed class RuntimeCheckA378Tests
{
    [Theory]
    [InlineData("VCRUNTIME140.dll", 1)]
    [InlineData("VCRUNTIME140_1.dll", 2)]
    public void A_missing_runtime_names_the_library_reports_identical_text_and_returns_three(string missing, int calls)
    {
        var loaded = new List<string>();
        var messages = new List<string>();
        using var errors = new StringWriter();
        var code = RuntimeCheck.Check(name => { loaded.Add(name); return name != missing; }, errors, messages.Add);
        Assert.Equal(3, code);
        Assert.Equal(calls, loaded.Count);
        Assert.Equal(missing, loaded.Last());
        var message = Assert.Single(messages);
        Assert.Contains($"BED couldn't load {missing}.", message);
        Assert.Contains("Microsoft Visual C++ Redistributable (x64)", message);
        Assert.Contains("Install or repair", message);
        Assert.Contains("https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist", message);
        Assert.Equal(message + errors.NewLine, errors.ToString());
    }

    [Fact]
    public void Both_libraries_load_in_order_without_a_failure_message()
    {
        var loaded = new List<string>();
        var messages = new List<string>();
        using var errors = new StringWriter();
        Assert.Equal(0, RuntimeCheck.Check(name => { loaded.Add(name); return true; }, errors, messages.Add));
        Assert.Equal(new[] { "VCRUNTIME140.dll", "VCRUNTIME140_1.dll" }, loaded);
        Assert.Empty(messages);
        Assert.Equal(string.Empty, errors.ToString());
    }

    [Fact]
    public void A_loader_bug_is_not_mislabeled_as_an_absent_runtime()
    {
        var error = new InvalidOperationException("injected loader fault");
        using var errors = new StringWriter();
        var messages = new List<string>();
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
            RuntimeCheck.Check(_ => throw error, errors, messages.Add)));
        Assert.Empty(messages);
        Assert.Equal(string.Empty, errors.ToString());
    }
}
