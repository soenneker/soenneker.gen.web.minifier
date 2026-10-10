using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Gen.Web.Minifier.BuildTasks.Abstract;

public interface IWebMinifierWriteRunner
{
    /// <summary>Scans configured folders and creates adjacent .min.css and .min.js files using the CSS and JavaScript minifiers. Existing minified files and directory links are never scanned as inputs. Unchanged output is not rewritten.</summary>
    /// <param name="args">Required --projectDir; optional --options (line-based settings file) and --outputs (generated-path manifest). Settings are folder, excludeFolder, cssEnabled, jsEnabled, and recursive, each followed by '=' and its value. Folder settings may repeat. Folders default to wwwroot, and both languages and recursion default to enabled. Relative paths are resolved against the project directory. Missing scan folders are skipped.</param>
    /// <param name="cancellationToken">Token used to cancel generation.</param>
    /// <returns>Zero on success; a nonzero exit code on failure.</returns>
    ValueTask<int> Run(string[] args, CancellationToken cancellationToken);
}
