using System;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.JavaScript.Minifier.Abstract;

namespace Soenneker.Gen.Web.Minifier.Tests;

internal sealed class CountingJavaScriptMinifier : IJavaScriptMinifier
{
    public int Calls { get; private set; }

    public ValueTask<string> Minify(string? javaScript, CancellationToken cancellationToken = default)
    {
        Calls++;
        return ValueTask.FromResult(javaScript!.Trim());
    }

    public ValueTask<string> MinifyFile(string filePath, bool log = true, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public ValueTask SaveMinifiedFile(string sourcePath, string? destinationPath = null, bool log = true, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
