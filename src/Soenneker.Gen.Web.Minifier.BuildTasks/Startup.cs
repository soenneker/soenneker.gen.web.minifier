using Microsoft.Extensions.DependencyInjection;
using Soenneker.Gen.Web.Minifier.BuildTasks.Abstract;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Soenneker.Css.Minify.Registrars;
using Soenneker.JavaScript.Minifier.Registrars;
using Soenneker.Utils.File.Registrars;
using Soenneker.Utils.Directory.Registrars;

namespace Soenneker.Gen.Web.Minifier.BuildTasks;

public static class Startup
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddCssMinifierAsSingleton().AddJavaScriptMinifierAsSingleton();
        services.AddFileUtilAsSingleton().AddDirectoryUtilAsSingleton();
        services.AddSingleton<IWebMinifierWriteRunner, WebMinifierWriteRunner>();
    }
}
