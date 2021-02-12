using LuceneServerNET.Client.Extensions;
using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Core.Models.Result;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LuceneServerNET.Client
{
    public class LuceneServerClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _serverUrl;
        private readonly string _indexName;

        private List<string> _refreshIndices = new List<string>();

        public LuceneServerClient(string serverUrl, string indexName, HttpClient httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            _serverUrl = serverUrl;
            _indexName = indexName;
        }

        async public Task<bool> CreateIndex()
        {
            using (var httpResponse = await _httpClient.GetAsync($"{ _serverUrl }/lucene/createindex/{ _indexName }"))
            {
                var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>();

                return apiResult.Success;
            }
        }

        async public Task<bool> IndexExists()
        {
            using (var httpResponse = await _httpClient.GetAsync($"{ _serverUrl }/lucene/indexexists/{ _indexName }"))
            {
                var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>();

                return apiResult.Success;
            }
        }

        async public Task<bool> RemoveIndex()
        {
            using (var httpResponse = await _httpClient.GetAsync($"{ _serverUrl }/lucene/removeindex/{ _indexName }"))
            {
                var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>();

                return apiResult.Success;
            }
        }

        async public Task<bool> RefreshIndex()
        {
            using (var httpResponse = await _httpClient.GetAsync($"{ _serverUrl }/lucene/refresh/{ _indexName }"))
            {
                var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>();

                if (_refreshIndices.Contains(_indexName))
                {
                    _refreshIndices.Remove(_indexName);
                }

                return apiResult.Success;
            }
        }

        async public Task<bool> Map(IndexMapping mapping)
        {
            HttpContent postContent = new StringContent(
                    JsonSerializer.Serialize(mapping),
                    Encoding.UTF8,
                    "application/json");

            using (var httpResponse = await _httpClient.PostAsync($"{ _serverUrl }/lucene/map/{ _indexName }", postContent))
            {
                var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>();

                return apiResult.Success;
            }
        } 

        async public Task<bool> IndexDocuments(IEnumerable<IDictionary<string,object>> documents)
        {
            HttpContent postContent = new StringContent(
                    JsonSerializer.Serialize(documents),
                    Encoding.UTF8,
                    "application/json");

            using (var httpResponse = await _httpClient.PostAsync($"{ _serverUrl }/lucene/index/{ _indexName }", postContent))
            {
                var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>();

                if(apiResult.Success)
                {
                    if (!_refreshIndices.Contains(_indexName))
                    {
                        _refreshIndices.Add(_indexName);
                    }
                }

                return apiResult.Success;
            }
        }

        public Task<bool> RemoveDocuments(IEnumerable<Guid> guids)
        {
            var term = String.Join(" OR ", guids.Select(g => g.ToString().ToLower()));

            return RemoveDocuments("_guid", term);
        }

        async public Task<bool> RemoveDocuments(string field, string term)
        {
            using (var httpResponse = await _httpClient.GetAsync($"{ _serverUrl }/lucene/remove/{ _indexName }?termField={ field }&term={ WebUtility.UrlEncode(term) }"))
            {
                var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>();

                if (apiResult.Success)
                {
                    if (!_refreshIndices.Contains(_indexName))
                    {
                        _refreshIndices.Add(_indexName);
                    }
                }

                return apiResult.Success;
            }
        }

        async public Task<LuceneSearchResult> Search(string query, IEnumerable<string> outFields = null)
        {
            string outFieldsString = outFields != null ?
                String.Join(",", outFields.Where(f => !String.IsNullOrEmpty(f)).Select(f => f.Trim())) :
                null;

            using (var httpResponse = await _httpClient.GetAsync($"{ _serverUrl }/lucene/search/{ _indexName }?outFields={ outFieldsString }&q={ WebUtility.UrlEncode(query) }"))
            {
                var apiResult = await httpResponse.DeserializeFromSuccessResponse<LuceneSearchResult>();

                return apiResult;
            }
        }

        #region IDispose

        public void Dispose()
        {
            foreach (var refreshIndex in _refreshIndices.Distinct())
            {
                Console.WriteLine($"Refresh index { refreshIndex }");
                try
                {
                    using (var httpResponse = _httpClient.GetAsync($"{ _serverUrl }/lucene/refresh/{ refreshIndex }").Result)
                    {
                        if (httpResponse.StatusCode == HttpStatusCode.OK)
                        {
                            Console.WriteLine("succeeded");
                        }
                        else
                        {
                            Console.WriteLine($"Warning: Status code { httpResponse.StatusCode }");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Waring: { ex.Message }");
                }
            }
        }

        #endregion
    }
}
