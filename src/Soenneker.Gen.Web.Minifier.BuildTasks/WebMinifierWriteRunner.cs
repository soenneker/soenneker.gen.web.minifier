using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Css.Minify.Abstract;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.Gen.Web.Minifier.BuildTasks.Abstract;
using Soenneker.Gen.Web.Minifier.BuildTasks.Dtos;
using Soenneker.JavaScript.Minifier.Abstract;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.Json;
using Soenneker.Utils.Runtime;

namespace Soenneker.Gen.Web.Minifier.BuildTasks;

public sealed class WebMinifierWriteRunner(
    ICssMinifier cssMinifier,
    IJavaScriptMinifier javaScriptMinifier,
    IFileUtil fileUtil,
    IDirectoryUtil directoryUtil) : IWebMinifierWriteRunner
{
    private static readonly StringComparer _pathComparer =
        RuntimeUtil.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public async ValueTask<int> Run(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i += 2)
            {
                if (i + 1 == args.Length || args[i] is not ("--projectDir" or "--options" or "--outputs") ||
                    !arguments.TryAdd(args[i], args[i + 1]))
                    throw new ArgumentException($"Invalid or duplicate argument: {args[i]}");
            }

            if (!arguments.TryGetValue("--projectDir", out string? projectDir) || string.IsNullOrWhiteSpace(projectDir))
                throw new ArgumentException("Missing required --projectDir.");
            projectDir = Path.GetFullPath(projectDir);
            if (!await directoryUtil.Exists(projectDir, cancellationToken).NoSync())
                throw new DirectoryNotFoundException($"Project directory not found: {projectDir}");

            var options = new WebMinifierOptions();
            if (arguments.TryGetValue("--options", out string? optionsPath))
            {
                foreach (string line in await fileUtil.ReadAsLines(Path.GetFullPath(optionsPath, projectDir),
                             log: false, cancellationToken: cancellationToken).NoSync())
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    int separator = line.IndexOf('=');
                    if (separator < 0)
                        throw new ArgumentException($"Invalid setting: {line}");
                    string key = line[..separator].Trim();
                    string value = line[(separator + 1)..].Trim();
                    switch (key)
                    {
                        case "folder" when value.Length > 0:
                            options.Folders.Add(value);
                            break;
                        case "excludeFolder" when value.Length > 0:
                            options.ExcludeFolders.Add(value);
                            break;
                        case "cssEnabled":
                            options.CssEnabled = bool.Parse(value);
                            break;
                        case "jsEnabled":
                            options.JsEnabled = bool.Parse(value);
                            break;
                        case "recursive":
                            options.Recursive = bool.Parse(value);
                            break;
                        default:
                            throw new ArgumentException($"Unknown or empty setting: {key}");
                    }
                }
            }

            if (options.Folders.Count == 0)
                options.Folders.Add("wwwroot");

            string cachePath = arguments.TryGetValue("--outputs", out string? manifestPath)
                ? Path.GetFullPath(manifestPath, projectDir) + ".cache"
                : Path.Combine(projectDir, "obj", "Soenneker.Gen.Web.Minifier.cache");
            var cache = new Dictionary<string, string>(_pathComparer);
            try
            {
                if (await fileUtil.Exists(cachePath, cancellationToken).NoSync())
                    cache = new Dictionary<string, string>(
                        JsonUtil.Deserialize<Dictionary<string, string>>(await fileUtil.Read(cachePath, log: false, cancellationToken: cancellationToken).NoSync())
                        ?? new Dictionary<string, string>(), _pathComparer);
            }
            catch (JsonException) { /* An invalid cache is rebuilt. */ }
            catch (IOException) { /* An unavailable cache is rebuilt. */ }

            // The build ID covers our code; the dependency manifest covers package/tool updates.
            string depsPath = Path.ChangeExtension(typeof(WebMinifierWriteRunner).Assembly.Location, ".deps.json");
            string toolHash = Hash(typeof(WebMinifierWriteRunner).Module.ModuleVersionId + "\n" +
                                   cssMinifier.GetType().Module.ModuleVersionId + "\n" +
                                   javaScriptMinifier.GetType().Module.ModuleVersionId + "\n" +
                                   (await fileUtil.Exists(depsPath, cancellationToken).NoSync()
                                       ? await fileUtil.Read(depsPath, log: false, cancellationToken: cancellationToken).NoSync()
                                       : ""));
            var updatedCache = new Dictionary<string, string>(_pathComparer);
            int skipped = 0;
            var outputs = new List<string>();
            var visited = new HashSet<string>(_pathComparer);
            foreach (string folder in options.Folders)
            {
                foreach (string input in await Scan(Path.GetFullPath(folder, projectDir), projectDir, options, visited,
                             cancellationToken).NoSync())
                {
                    string extension = Path.GetExtension(input).ToLowerInvariant();
                    if (!(extension == ".css" && options.CssEnabled || extension == ".js" && options.JsEnabled) ||
                        Path.GetFileNameWithoutExtension(input).EndsWith(".min", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string output = Path.ChangeExtension(input, ".min" + extension);
                    if (await fileUtil.Exists(output, cancellationToken).NoSync() &&
                        (File.GetAttributes(output) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException($"Refusing to overwrite a linked output: {output}");
                    string source = await fileUtil.Read(input, log: false, cancellationToken: cancellationToken)
                                                  .NoSync();
                    string? existing = await fileUtil.Exists(output, cancellationToken).NoSync()
                        ? await fileUtil.Read(output, log: false, cancellationToken: cancellationToken).NoSync()
                        : null;
                    string inputHash = toolHash + Hash(source);
                    if (existing is not null && cache.TryGetValue(input, out string? cached) && cached == inputHash + Hash(existing))
                    {
                        updatedCache[input] = cached;
                        outputs.Add(Path.GetRelativePath(projectDir, output));
                        skipped++;
                        continue;
                    }
                    string minified = extension == ".css"
                        ? await cssMinifier.Minify(source, cancellationToken).NoSync()
                        : await javaScriptMinifier.Minify(source, cancellationToken).NoSync();
                    if (existing != minified)
                        await fileUtil.WriteAtomically(output, minified, log: false, cancellationToken: cancellationToken)
                                      .NoSync();
                    updatedCache[input] = inputHash + Hash(minified);
                    outputs.Add(Path.GetRelativePath(projectDir, output));
                }
            }

            if (arguments.TryGetValue("--outputs", out string? outputsPath))
            {
                outputsPath = Path.GetFullPath(outputsPath, projectDir);
                await directoryUtil.Create(Path.GetDirectoryName(outputsPath)!, log: false,
                    cancellationToken: cancellationToken).NoSync();
                string outputManifest = string.Join(Environment.NewLine, outputs) + (outputs.Count > 0 ? Environment.NewLine : string.Empty);
                await fileUtil.WriteAtomically(outputsPath, outputManifest, log: false, cancellationToken: cancellationToken)
                              .NoSync();
            }

            await directoryUtil.Create(Path.GetDirectoryName(cachePath)!, log: false,
                cancellationToken: cancellationToken).NoSync();
            string cacheJson = JsonUtil.Serialize(updatedCache) ?? throw new InvalidOperationException("Failed to serialize the minifier cache.");
            await fileUtil.WriteAtomically(cachePath, cacheJson, log: false, cancellationToken: cancellationToken).NoSync();

            Console.WriteLine($"WebMinifier: minified {outputs.Count - skipped} asset(s), skipped {skipped} unchanged asset(s).");
            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync($"WebMinifier: {exception.Message}").NoSync();
            return 1;
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private async ValueTask<List<string>> Scan(string folder, string projectDir, WebMinifierOptions options,
        HashSet<string> visited, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = new List<string>();
        if (!await directoryUtil.Exists(folder, cancellationToken).NoSync() ||
            !visited.Add(Path.TrimEndingDirectorySeparator(folder)) ||
            (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0 || IsExcluded(folder, projectDir, options))
            return files;

        List<string> candidates = await directoryUtil
                                        .GetFilesByExtension(folder, string.Empty, recursive: false,
                                            cancellationToken: cancellationToken).NoSync();
        foreach (string file in candidates.OrderBy(static path => path, _pathComparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                files.Add(file);
        }

        if (!options.Recursive)
            return files;
        List<string> directories = await directoryUtil.GetAllDirectories(folder, cancellationToken).NoSync();
        foreach (string child in directories.OrderBy(static path => path, _pathComparer))
            files.AddRange(await Scan(child, projectDir, options, visited, cancellationToken).NoSync());
        return files;
    }

    private static bool IsExcluded(string folder, string projectDir, WebMinifierOptions options)
    {
        foreach (string excluded in options.ExcludeFolders)
        {
            if (!Path.IsPathRooted(excluded) && !excluded.Contains('/') && !excluded.Contains('\\'))
            {
                if (Path.GetRelativePath(projectDir, folder)
                        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Any(part => _pathComparer.Equals(part, excluded)))
                    return true;
            }
            else
            {
                string excludedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(excluded, projectDir));
                if (_pathComparer.Equals(folder, excludedPath) || folder.StartsWith(
                        excludedPath + Path.DirectorySeparatorChar,
                        RuntimeUtil.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }
}
