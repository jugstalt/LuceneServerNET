using LuceneServerNET.Services.Abstraction;
using System.Reflection;

namespace LuceneServerNET.Services
{
    public class AppVersionService : IAppVersionService
    {
        //public string Version =>
        //    Assembly
        //    .GetAssembly(typeof(LuceneServerNET.Core.Models.Result.ApiResult))
        //    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        //    .InformationalVersion;

        public string Version =>
            Assembly
            .GetAssembly(typeof(LuceneServerNET.Core.Models.Result.ApiResult))!
            .GetName()!
            .Version!.ToString();
    }
}
