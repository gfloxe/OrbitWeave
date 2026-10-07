namespace OrbitWeave.Tests;

public class CrashLogTests
{
    [Fact]
    public void Screen_conversion_really_throws_without_a_presentation_source() =>
        Assert.Throws<InvalidOperationException>(() => new System.Windows.Shapes.Rectangle().PointFromScreen(new System.Windows.Point()));

    [Fact]
    public void Journal_includes_full_exception_chain_and_build_context()
    {
        Exception exception;
        try { throw new InvalidOperationException("outer", new ArgumentException("inner")); }
        catch (Exception ex) { exception = ex; }
        var text = CrashLog.Format("test", exception, true);
        Assert.Contains("outer", text); Assert.Contains("inner", text);
        Assert.Contains(nameof(Journal_includes_full_exception_chain_and_build_context), text);
        Assert.Contains("version=", text); Assert.Contains("exe=", text); Assert.Contains("terminating=True", text);
        Assert.Contains("pid=", text); Assert.Contains("thread=", text);
    }

    [Fact]
    public async Task Concurrent_errors_are_appended_without_overwriting_each_other()
    {
        var root = Path.Combine(Path.GetTempPath(), "orbit-crash-test-" + Guid.NewGuid());
        var path = Path.Combine(root, "crash.log");
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
                Assert.True(CrashLog.Write($"context-{index}", new Exception($"error-{index}"), path: path)))));
            var text = File.ReadAllText(path);
            Assert.Equal(12, text.Split("version=").Length - 1);
        }
        finally { Directory.Delete(root, true); }
    }
}
