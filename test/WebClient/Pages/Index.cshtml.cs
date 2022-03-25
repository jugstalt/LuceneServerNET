using LuceneServerNET.Client;
using LuceneServerNET.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace WebClient.Pages
{
    public class IndexModel : PageModel
    {
        const string IndexName = "feedclient";
        static LuceneServerClient _client = new LuceneServerClient("https://localhost:44393", IndexName);
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(ILogger<IndexModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {

        }

        async public Task<IActionResult> OnGetSearch(string term)
        {
            var termParser = new TermParser();
            var result = await _client.SearchAsync($"{ termParser.Parse(term) }");

            return new JsonResult(new
            {
                items = result.Hits
            });
        }
    }
}
