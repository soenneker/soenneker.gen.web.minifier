using System.Collections.Generic;

namespace Soenneker.Gen.Web.Minifier.BuildTasks.Dtos;

internal sealed class WebMinifierOptions
{
    public bool CssEnabled { get; set; } = true;
    public bool JsEnabled { get; set; } = true;
    public bool Recursive { get; set; } = true;
    public List<string> Folders { get; } = [];
    public List<string> ExcludeFolders { get; } = ["bin", "obj", "node_modules", ".git"];
}
