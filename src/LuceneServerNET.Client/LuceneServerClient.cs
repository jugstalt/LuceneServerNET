using LuceneServerNET.Client.Extensions;
using LuceneServerNET.Core.Models.Custom;
using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Core.Models.Result;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
        private IndexMapping _mapping = null;

        private readonly string _clientId, _clientSecret;

        private List<string> _refreshIndices = new List<string>();

        public LuceneServerClient(string serverUrl,
                                  string indexName,
                                  HttpClient httpClient = null)
            : this(serverUrl, indexName, String.Empty, String.Empty, httpClient)
        {

        }

        public LuceneServerClient(string serverUrl, 
                                  string indexName,
                                  string clientId, string clientSecret,
                                  HttpClient httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            _serverUrl = serverUrl;
            _indexName = indexName;

            var uri = new Uri(_serverUrl);
            var userInfo = uri.UserInfo;

            if (String.IsNullOrEmpty(clientId) &&
                String.IsNullOrEmpty(clientSecret) &&
                !String.IsNullOrEmpty(userInfo) && userInfo.Contains(":"))
            {
                _clientId = userInfo.Substring(0, userInfo.IndexOf(':'));
                _clientSecret = userInfo.Substring(userInfo.IndexOf(':') + 1);

                _serverUrl = _serverUrl.Replace($"{ userInfo }@", "");
            }
            else
            {
                _clientId = clientId;
                _clientSecret = clientSecret;
            }
        }

        async public Task<bool> CreateIndexAsync()
        {
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{ _serverUrl }/lucene/createindex/{ _indexName }"))
            {
                ModifyHttpRequest(requestMessage);

                using (var httpResponse = await _httpClient.SendAsync(requestMessage))
                {
                    var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>();

                    return apiResult.Success;
                }
            }
        }

        async public Task<bool> IndexExistsAsync()
        {
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{ _serverUrl }/lucene/indexexists/{ _indexName }"))
            {
                ModifyHttpRequest(requestMessage);

                using (var httpResponse = await _httpClient.SendAsync(requestMessage))
                {
                    var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>(false);

                    return apiResult.Success;
                }
            }
        }

        async public Task<bool> RemoveIndexAsync()
        {
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{ _serverUrl }/lucene/removeindex/{ _indexName }"))
            {
                ModifyHttpRequest(requestMessage);

                using (var httpResponse = await _httpClient.SendAsync(requestMessage))
                {
                    var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>();

                    return apiResult.Success;
                }
            }
        }

        async public Task<bool> RefreshIndex()
        {
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{ _serverUrl }/lucene/refresh/{ _indexName }"))
            {
                ModifyHttpRequest(requestMessage);

                using (var httpResponse = await _httpClient.SendAsync(requestMessage))
                {
                    var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>();

                    if (_refreshIndices.Contains(_indexName))
                    {
                        _refreshIndices.Remove(_indexName);
                    }

                    return apiResult.Success;
                }
            }
        }

        #region Metadata

        #region Mapping

        async public Task<bool> MapAsync(IndexMapping mapping)
        {
            HttpContent postContent = new StringContent(
                    JsonSerializer.Serialize(mapping),
                    Encoding.UTF8,
                    "application/json");

            using (var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{ _serverUrl }/lucene/map/{ _indexName }"))
            {
                ModifyHttpRequest(requestMessage);
                requestMessage.Content = postContent;

                using (var httpResponse = await _httpClient.SendAsync(requestMessage))
                {
                    var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>();

                    return apiResult.Success;
                }
            }
        } 

        async public Task<LuceneMappingResult> MappingAsync()
        {
            if (_mapping == null)
            {
                using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{ _serverUrl }/lucene/mapping/{ _indexName }"))
                {
                    ModifyHttpRequest(requestMessage);

                    using (var httpResponse = await _httpClient.SendAsync(requestMessage))
                    {
                        var apiResult = await httpResponse.DeserializeFromSuccessResponse<LuceneMappingResult>();

                        if (apiResult != null)
                        {
                            _mapping = apiResult.Mapping;
                        }

                        return apiResult;
                    }
                }
            } else
            {
                return new LuceneMappingResult() { Mapping = _mapping };
            }
        }

        #endregion

        #region Custom Metadata

        async public Task<bool> AddCustomMetadataAsync(string name, string metadata)
        {
            var customMeta = new CustomMetadata()
            {
                Metadata = metadata
            };

            HttpContent postContent = new StringContent(
                   JsonSerializer.Serialize(customMeta),
                   Encoding.UTF8,
                   "application/json");

            using (var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{ _serverUrl }/lucene/addmeta/{ _indexName }?name={ name }"))
            {
                ModifyHttpRequest(requestMessage);
                requestMessage.Content = postContent;
                
                using (var httpResponse = await _httpClient.SendAsync(requestMessage))
                {
                    var apiResult = await httpResponse.DeserializeFromSuccessResponse<ApiResult>();

                    return apiResult.Success;
                }
            }
        }

        async public Task<CustomMetadataResult> GetCustomMetadataAsync(string name)
        {
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{ _serverUrl }/lucene/getmeta/{ _indexName }?name={ name }"))
            {
                ModifyHttpRequest(requestMessage);

                using (var httpResponse = await _httpClient.SendAsync(requestMessage))
                {
                    var apiResult = await httpResponse.DeserializeFromSuccessResponse<CustomMetadataResult>();

                    return apiResult;
                }
            }
        }

        async public Task<IEnumerable<string>> GetCustomMetadataNamesAsync()
        {
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{ _serverUrl }/lucene/getmetanames/{ _indexName }"))
            {
                ModifyHttpRequest(requestMessage);

                using (var httpResponse = await _httpClient.SendAsync(requestMessage))
                {
                    var apiResult = await httpResponse.DeserializeFromSuccessResponse<LuceneGenericListResult<string>>();

                    return apiResult.Result;
                }
            }
        }

        async public Task<IDictionary<string, string>> GetCustomMetadatasAsync()
        {
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{ _serverUrl }/lucene/getmetas/{ _indexName }"))
            {
                ModifyHttpRequest(requestMessage);

                using (var httpResponse = await _httpClient.SendAsync(requestMessage))
                {
                    var apiResult = await httpResponse.DeserializeFromSuccessResponse<CustomMetadatasResult>();

                    return apiResult.Metadata;
                }
            }
        }

        #endregion

        #endregion

        async public Task<bool> IndexDocumentsAsync(IEnumerable<IDictionary<string,object>> documents)
        {
            HttpContent postContent = new StringContent(
                    JsonSerializer.Serialize(documents),
                    Encoding.UTF8,
                    "application/json");

            using (var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{ _serverUrl }/lucene/index/{ _indexName }"))
            {
                ModifyHttpRequest(requestMessage);
                requestMessage.Content = postContent;

                using (var httpResponse = await _httpClient.SendAsync(requestMessage))
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
        }

        public Task<bool> RemoveDocumentsAsync(IEnumerable<Guid> guids)
        {
            var term = String.Join(" OR ", guids.Select(g => g.ToString().ToLower()));

            return RemoveDocumentsAsync("_guid", term);
        }

        async public Task<bool> RemoveDocumentsAsync(string field, string term)
        {
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{ _serverUrl }/lucene/remove/{ _indexName }?termField={ field }&term={ WebUtility.UrlEncode(term) }"))
            {
                ModifyHttpRequest(requestMessage);

                using (var httpResponse = await _httpClient.SendAsync(requestMessage))
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
        }

        async public Task<LuceneSearchResult> SearchAsync(string query, 
                                                          IEnumerable<string> outFields = null, 
                                                          int size = 20,
                                                          string sortField = "",
                                                          bool sortReverse = false)
        {
            var mapping = await CurrentIndexMapping();

            using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{ _serverUrl }/lucene/search/{ _indexName }?outFields={ outFields.ToOutFieldsParameterString() }&sortField={ sortField }&sortReverse={ sortReverse }&size={ size }&q={ WebUtility.UrlEncode(query) }"))
            {
                ModifyHttpRequest(requestMessage);

                using (var httpResponse = await _httpClient.SendAsync(requestMessage))
                {
                    var apiResult = await httpResponse.DeserializeFromSuccessResponse<LuceneSearchResult>();

                    if (apiResult.Success && apiResult.Hits != null)
                    {
                        foreach (var hit in apiResult.Hits)
                        {
                            foreach (var key in hit.Keys.ToArray())
                            {
                                var field = mapping?.GetField(key);
                                if (field != null)
                                {
                                    hit[key] = field.ToValueType(hit[key]);
                                }
                                else
                                {
                                    hit[key] = hit[key]?.ToString();
                                }
                            }
                        }
                    }

                    return apiResult;
                }
            }
        }

        async public Task<LuceneGroupResult> GroupAsync(string groupField, string query = "")
        {
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{ _serverUrl }/lucene/group/{ _indexName }?groupField={ groupField }&q={ WebUtility.UrlEncode(query) }"))
            {
                ModifyHttpRequest(requestMessage);

                using (var httpResponse = await _httpClient.SendAsync(requestMessage))
                {
                    var apiResult = await httpResponse.DeserializeFromSuccessResponse<LuceneGroupResult>();

                    if (apiResult.Success && apiResult.Hits != null)
                    {
                        foreach (var hit in apiResult.Hits)
                        {
                            foreach (var key in hit.Keys.ToArray())
                            {
                                switch (key)
                                {
                                    case "value":
                                        hit[key] = hit[key]?.ToString();
                                        break;
                                    case "_hits":
                                        hit[key] = int.Parse(hit[key].ToString());
                                        break;
                                }
                            }
                        }
                    }

                    return apiResult;
                }
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
                    using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{ _serverUrl }/lucene/refresh/{ refreshIndex }"))
                    {
                        ModifyHttpRequest(requestMessage);
                        
                        using (var httpResponse = _httpClient.SendAsync(requestMessage).Result)
                        {
                            var apiResult = httpResponse.DeserializeFromSuccessResponse<ApiResult>().Result;

                            Console.WriteLine($"Info - Refreshing index { refreshIndex }: succeeded");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Waring - Refreshing index { refreshIndex }: { ex.Message }");
                }
            }
        }

        #endregion

        #region Helper

        async private Task<IndexMapping> CurrentIndexMapping() => _mapping ?? (await MappingAsync()).Mapping;

        private void ModifyHttpRequest(HttpRequestMessage requestMessage)
        {
            if (!String.IsNullOrEmpty(_clientId) && !String.IsNullOrEmpty(_clientSecret))
            {
                // Add Basic Auth
                var authenticationString = $"{ _clientId }:{ _clientSecret }";
                var base64EncodedAuthenticationString = Convert.ToBase64String(System.Text.ASCIIEncoding.ASCII.GetBytes(authenticationString));

                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
            }
        }

        #endregion
    }
}
