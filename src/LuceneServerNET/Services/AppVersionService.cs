using LuceneServerNET.Services.Abstraction;
using System.Reflection;

namespace LuceneServerNET.Services
{
    public class AppVersionService : IAppVersionService
    {
        // The version comes from the git tag (MinVer), e.g. "3.1.0" or "3.2.0-preview.1".
        // AssemblyVersion is only major.0.0.0 (binding compatibility), so use the informational version
        // without the commit hash MinVer/SourceLink append ("3.1.0+abc123...").
        public string Version { get; } = ReadVersion();

        private static string ReadVersion()
        {
            var assembly = Assembly.GetAssembly(typeof(LuceneServerNET.Core.Models.Result.ApiResult))!;
            var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                var plus = informationalVersion.IndexOf('+');
                return plus > 0 ? informationalVersion[..plus] : informationalVersion;
            }

            return assembly.GetName().Version!.ToString();
        }
    }
}
