using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.Gen.Web.Minifier.BuildTasks;
using Soenneker.Gen.Web.Minifier.BuildTasks.Abstract;

namespace Soenneker.Gen.Web.Minifier.Tests;

public sealed class WebMinifierTests
{
    [Test]
    public async Task Scanning_configuration_and_real_minification()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        Startup.ConfigureServices(services);
        await using ServiceProvider provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<IWebMinifierWriteRunner>();
        string root = Path.Combine(Path.GetTempPath(), "web minifier " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        CancellationToken ct = timeout.Token;
        try
        {
            Check(await runner.Run([], ct).NoSync() == 1, "Missing arguments must fail.");
            Write(root, "assets/site.js", "export function add(first, second) { /* remove */ return first + second; }");
            Write(root, "assets/site.css", "body { /* remove */ color: red; padding: 0px 0px 0px 0px; }");
            Write(root, "assets/nested/child.js", "console.log( 1 + 2 );");
            Write(root, "assets/vendor/ignored.js", "const = ;");
            Write(root, "assets/node_modules/ignored.js", "const = ;");
            Write(root, "assets/existing.min.js", "already minified");
            Write(root, "outside.js", "const = ;");
            Write(root, "options", "folder=assets\nfolder=assets/nested\nexcludeFolder=assets/vendor");
            string[] args = ["--projectDir", root, "--options", "options", "--outputs", "outputs"];
            Check(await runner.Run(args, ct).NoSync() == 0, "Minification failed.");
            Check(File.ReadAllText(Path.Combine(root, "assets/site.min.js")).Contains("export"), "ES module was not preserved.");
            Check(!File.ReadAllText(Path.Combine(root, "assets/site.min.css")).Contains("remove"), "CSS was not minified.");
            Check(File.ReadAllLines(Path.Combine(root, "outputs")).Length == 3, "Overlapping folders were not deduplicated.");
            Check(!File.Exists(Path.Combine(root, "assets/existing.min.min.js")), "Minified input was processed.");
            DateTime modified = File.GetLastWriteTimeUtc(Path.Combine(root, "assets/site.min.js"));
            Check(await runner.Run(args, ct).NoSync() == 0, "Second run failed.");
            Check(File.GetLastWriteTimeUtc(Path.Combine(root, "assets/site.min.js")) == modified, "Unchanged output was rewritten.");

            Write(root, "switches/top.js", "console.log( 4 );");
            Write(root, "switches/top.css", "p { color: blue; }");
            Write(root, "switches/nested/child.js", "console.log( 5 );");
            Write(root, "options", "folder=switches\ncssEnabled=false\nrecursive=false");
            Check(await runner.Run(args, ct).NoSync() == 0, "JS-only run failed.");
            Check(File.Exists(Path.Combine(root, "switches/top.min.js")), "JS output missing.");
            Check(!File.Exists(Path.Combine(root, "switches/top.min.css")), "Disabled CSS was processed.");
            Check(!File.Exists(Path.Combine(root, "switches/nested/child.min.js")), "Recursion switch ignored.");
            Write(root, "switches/top.js", "const = ;");
            Write(root, "options", "folder=switches\njsEnabled=false");
            Check(await runner.Run(args, ct).NoSync() == 0, "CSS-only run processed invalid JS.");
            Check(File.Exists(Path.Combine(root, "switches/top.min.css")), "CSS output missing.");

            string previous = File.ReadAllText(Path.Combine(root, "switches/top.min.js"));
            Write(root, "options", "folder=switches\ncssEnabled=false");
            Check(await runner.Run(args, ct).NoSync() == 1, "Invalid JS must fail.");
            Check(File.ReadAllText(Path.Combine(root, "switches/top.min.js")) == previous, "Failed minification overwrote output.");
            Write(root, "options", "recursive=invalid");
            Check(await runner.Run(args, ct).NoSync() == 1, "Malformed settings must fail.");
            Check(await runner.Run(["--projectDir", root], ct).NoSync() == 0, "Missing default wwwroot should be allowed.");
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            try
            {
                await runner.Run(["--projectDir", root], canceled.Token).NoSync();
                throw new Exception("Cancellation ignored.");
            }
            catch (OperationCanceledException) { }
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Write(string root, string path, string text)
    {
        string fullPath = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, text);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
