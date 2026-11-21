using System.IO;
using System.Linq;
using Xunit;

// Unity WebGL has no threads: a continuation sent to the thread pool (ConfigureAwait(false)) never runs there. In the
// browser the score submission after "game_end" never happened. The core must keep continuations on the caller's
// context (Unity's main thread).
public class WebGLSafetyTests
{
    static string CoreDir()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "unity", "com.tupic.sdk", "Runtime", "Core"))) d = d.Parent;
        return Path.Combine(d.FullName, "unity", "com.tupic.sdk", "Runtime", "Core");
    }

    [Fact]
    public void CoreNeverSendsContinuationsToTheThreadPool()
    {
        var offenders = Directory.GetFiles(CoreDir(), "*.cs")
            .Where(f => File.ReadAllText(f).Contains("ConfigureAwait(false)")).Select(Path.GetFileName).ToList();
        Assert.Empty(offenders);
    }
}
