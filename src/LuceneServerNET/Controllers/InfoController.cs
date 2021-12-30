using LuceneServerNET.Services.Abstraction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuceneServerNET.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class InfoController : ControllerBase
    {
        private readonly IAppVersionService _appVersionService;
        public InfoController(IAppVersionService appVersionService)
        {
            _appVersionService = appVersionService;
        }

        [HttpGet]
        [Route("")]
        public object Info()
        {
            return new
            {
                Version = _appVersionService.Version
            };
        }
    }
}
