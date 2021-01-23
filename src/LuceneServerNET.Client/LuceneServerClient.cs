using LuceneServerNET.Client.Extensions;
using LuceneServerNET.Core.Models.Result;
using System.Net.Http;
using System.Threading.Tasks;

namespace LuceneServerNET.Client
{
    public class LuceneServerClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _serverUrl;

        public LuceneServerClient(string serverUrl, HttpClient httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            _serverUrl = serverUrl;
        }

        async public Task<bool> CreateIndex(string indexName)
        {
            var httpResponse = await _httpClient.GetAsync($"{ _serverUrl }/lucene/create/{ indexName }");

            var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>();

            return apiResult.Success;
        }
    }
}
