using LuceneServerNET.Client.Extensions;
using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Core.Models.Result;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
            using (var httpResponse = await _httpClient.GetAsync($"{ _serverUrl }/lucene/createindex/{ indexName }"))
            {

                var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>();

                return apiResult.Success;
            }
        }

        async public Task<bool> RemoveIndex(string indexName)
        {
            using (var httpResponse = await _httpClient.GetAsync($"{ _serverUrl }/lucene/removeindex/{ indexName }"))
            {

                var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>();

                return apiResult.Success;
            }
        }

        async public Task<bool> Map(string indexName, IndexMapping mapping)
        {
            HttpContent postContent = new StringContent(
                    JsonSerializer.Serialize(mapping),
                    Encoding.UTF8,
                    "application/json");

            using (var httpResponse = await _httpClient.PostAsync($"{ _serverUrl }/lucene/map/{ indexName }", postContent))
            {
                var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>();

                return apiResult.Success;
            }
        } 

        async public Task<bool> IndexItems(string indexName, IEnumerable<IDictionary<string,object>> items)
        {
            HttpContent postContent = new StringContent(
                    JsonSerializer.Serialize(items),
                    Encoding.UTF8,
                    "application/json");

            using (var httpResponse = await _httpClient.PostAsync($"{ _serverUrl }/lucene/index/{ indexName }", postContent))
            {
                var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>();

                return apiResult.Success;
            }
        }

        async public Task<LuceneSearchResult> Search(string indexName, string query)
        {
            using (var httpResponse = await _httpClient.GetAsync($"{ _serverUrl }/lucene/search/{ indexName }?q={ WebUtility.UrlEncode(query) }"))
            {
                var apiResult = await httpResponse.DeserializeFromSuccessResponse<LuceneSearchResult>();

                return apiResult;
            }
        }
    }
}
