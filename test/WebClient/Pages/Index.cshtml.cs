using LuceneServerNET.Client;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WebClient.Pages
{
    public class IndexModel : PageModel
    {
        static LuceneServerClient _client = new LuceneServerClient("https://localhost:44393");
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
            var result = await _client.Search("new-index", $"{ term }*");

            return new JsonResult(new
            {
                items = result.Hits
            });
        }
    }
}
