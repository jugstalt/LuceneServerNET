using LuceneServerNET.Client.Exceptions;
using LuceneServerNET.Core.Extensions;
using LuceneServerNET.Core.Models.Admin;
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
using System.Threading;
using System.Threading.Tasks;

namespace LuceneServerNET.Client
{
    /// <summary>
    /// Client for the resource oriented API v2 (/api/v2/...). Not bound to a single index.
    /// Errors (HTTP status codes other than 2xx) are thrown as <see cref="LuceneServerClientException"/>.
    /// </summary>
    public class LuceneServerV2Client
    {
        private static readonly HttpClient SharedHttpClient = new HttpClient();

        private readonly HttpClient _httpClient;
        private readonly string _clientId, _clientSecret;

        public LuceneServerV2Client(string serverUrl,
                                    string clientId = null,
                                    string clientSecret = null,
                                    HttpClient httpClient = null)
        {
            if (String.IsNullOrWhiteSpace(serverUrl))
            {
                throw new ArgumentException("serverUrl is empty", nameof(serverUrl));
            }

            _httpClient = httpClient ?? SharedHttpClient;
            ServerUrl = serverUrl.TrimEnd('/');
            _clientId = clientId;
            _clientSecret = clientSecret;
        }

        public string ServerUrl { get; }

        #region Admin

        public async Task<ServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default)
            => (await SendAsync<ServerInfoResult>(HttpMethod.Get, "api/v2/admin/info", cancellationToken: cancellationToken)).Info;

        public Task<WhoAmIResult> WhoAmIAsync(CancellationToken cancellationToken = default)
            => SendAsync<WhoAmIResult>(HttpMethod.Get, "api/v2/admin/whoami", cancellationToken: cancellationToken);

        /// <summary>Calls the (anonymous) /health endpoint. Returns the status text or throws.</summary>
        public async Task<string> GetHealthAsync(CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ServerUrl}/health");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return response.IsSuccessStatusCode ? body : throw new LuceneServerClientException($"Health check returns HTTP status code {response.StatusCode}: {body}");
        }

        #endregion

        #region Indices

        public async Task<IEnumerable<string>> GetIndicesAsync(CancellationToken cancellationToken = default)
            => (await SendAsync<LuceneGenericListResult<string>>(HttpMethod.Get, "api/v2/indices", cancellationToken: cancellationToken)).Result ?? Array.Empty<string>();

        public async Task<bool> IndexExistsAsync(string index, CancellationToken cancellationToken = default)
        {
            using var request = CreateRequest(HttpMethod.Get, $"api/v2/indices/{Esc(index)}");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
            await ReadResultAsync<ApiResult>(response).ConfigureAwait(false);
            return true;
        }

        public async Task<IndexStats> GetIndexStatsAsync(string index, CancellationToken cancellationToken = default)
            => (await SendAsync<IndexStatsResult>(HttpMethod.Get, $"api/v2/indices/{Esc(index)}/stats", cancellationToken: cancellationToken)).Stats;

        public async Task CreateIndexAsync(string index, CancellationToken cancellationToken = default)
            => await SendAsync<ApiResult>(HttpMethod.Put, $"api/v2/indices/{Esc(index)}", cancellationToken: cancellationToken);

        public async Task RemoveIndexAsync(string index, CancellationToken cancellationToken = default)
            => await SendAsync<ApiResult>(HttpMethod.Delete, $"api/v2/indices/{Esc(index)}", cancellationToken: cancellationToken);

        public async Task RefreshIndexAsync(string index, CancellationToken cancellationToken = default)
            => await SendAsync<ApiResult>(HttpMethod.Post, $"api/v2/indices/{Esc(index)}/refresh", cancellationToken: cancellationToken);

        #endregion

        #region Aliases

        /// <summary>All aliases (alias => index).</summary>
        public async Task<IDictionary<string, string>> GetAliasesAsync(CancellationToken cancellationToken = default)
            => (await SendAsync<AliasesResult>(HttpMethod.Get, "api/v2/aliases", cancellationToken: cancellationToken)).Aliases
               ?? new Dictionary<string, string>();

        /// <summary>Creates or switches an alias to the index.</summary>
        public async Task SetAliasAsync(string alias, string index, CancellationToken cancellationToken = default)
            => await SendAsync<ApiResult>(HttpMethod.Put, $"api/v2/aliases/{Esc(alias)}", new { index }, cancellationToken);

        public async Task RemoveAliasAsync(string alias, CancellationToken cancellationToken = default)
            => await SendAsync<ApiResult>(HttpMethod.Delete, $"api/v2/aliases/{Esc(alias)}", cancellationToken: cancellationToken);

        #endregion

        #region Mapping / Meta

        public async Task<IndexMapping> GetMappingAsync(string index, CancellationToken cancellationToken = default)
            => (await SendAsync<MappingResult>(HttpMethod.Get, $"api/v2/indices/{Esc(index)}/mapping", cancellationToken: cancellationToken)).Mapping;

        public async Task PutMappingAsync(string index, IndexMapping mapping, CancellationToken cancellationToken = default)
            => await SendAsync<ApiResult>(HttpMethod.Put, $"api/v2/indices/{Esc(index)}/mapping", mapping, cancellationToken);

        public async Task<IDictionary<string, string>> GetMetadataAsync(string index, CancellationToken cancellationToken = default)
            => (await SendAsync<CustomMetadatasResult>(HttpMethod.Get, $"api/v2/indices/{Esc(index)}/meta", cancellationToken: cancellationToken)).Metadata
               ?? new Dictionary<string, string>();

        public async Task PutMetadataAsync(string index, string name, string value, CancellationToken cancellationToken = default)
            => await SendAsync<ApiResult>(HttpMethod.Put, $"api/v2/indices/{Esc(index)}/meta/{Esc(name)}", new CustomMetadata() { Metadata = value }, cancellationToken);

        #endregion

        #region Documents

        public async Task AddDocumentsAsync(string index, IEnumerable<IDictionary<string, object>> documents, CancellationToken cancellationToken = default)
            => await SendAsync<ApiResult>(HttpMethod.Post, $"api/v2/indices/{Esc(index)}/documents", documents, cancellationToken);

        /// <summary>Number of documents announced by the export (header X-Total-Count), set when the export starts.</summary>
        public delegate void ExportStartedHandler(long? totalCount);

        /// <summary>
        /// Streams all documents of the index (stored fields, without _guid) from GET .../documents/_export (NDJSON).
        /// Values are <see cref="JsonElement"/>s.
        /// </summary>
        public async IAsyncEnumerable<IDictionary<string, object>> ExportDocumentsAsync(string index,
                                                                                         ExportStartedHandler onStarted = null,
                                                                                         [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var request = CreateRequest(HttpMethod.Get, $"api/v2/indices/{Esc(index)}/documents/_export");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                await ReadResultAsync<ApiResult>(response).ConfigureAwait(false);   // throws with the error message
            }

            long? total = null;
            if (response.Headers.TryGetValues("X-Total-Count", out var values) &&
                long.TryParse(values.FirstOrDefault(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var t))
            {
                total = t;
            }
            onStarted?.Invoke(total);

            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var reader = new System.IO.StreamReader(stream, Encoding.UTF8);

            string line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (String.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                yield return JsonSerializer.Deserialize<Dictionary<string, object>>(line);
            }
        }

        /// <summary>Removes all documents matching the term (Lucene query syntax) in field.</summary>
        public async Task RemoveDocumentsAsync(string index, string term, string field = "_guid", CancellationToken cancellationToken = default)
            => await SendAsync<ApiResult>(HttpMethod.Delete, $"api/v2/indices/{Esc(index)}/documents?term={Esc(term)}&field={Esc(field)}", cancellationToken: cancellationToken);

        #endregion

        #region Search

        public Task<LuceneSearchResult> SearchAsync(string index, SearchRequest request, CancellationToken cancellationToken = default)
            => SendAsync<LuceneSearchResult>(HttpMethod.Get, SearchPath(index, request), cancellationToken: cancellationToken);

        public async Task<IEnumerable<IDictionary<string, object>>> GroupAsync(string index, string field, string q = null, CancellationToken cancellationToken = default)
            => (await SendAsync<LuceneGroupResult>(HttpMethod.Get, $"api/v2/indices/{Esc(index)}/groups?field={Esc(field)}&q={Esc(q)}", cancellationToken: cancellationToken)).Hits
               ?? Array.Empty<IDictionary<string, object>>();

        /// <summary>Relative path (incl. query string) of a search request, e.g. to show it as curl command.</summary>
        public static string SearchPath(string index, SearchRequest request)
        {
            var sb = new StringBuilder($"api/v2/indices/{Esc(index)}/search{(request.Phonetic ? "/phonetic" : "")}?q={Esc(request.Query)}");
            if (!String.IsNullOrWhiteSpace(request.OutFields)) sb.Append($"&outFields={Esc(request.OutFields)}");
            if (request.From > 0) sb.Append($"&from={request.From}");
            sb.Append($"&size={request.Size}");
            if (!String.IsNullOrWhiteSpace(request.SearchAfter)) sb.Append($"&searchAfter={Esc(request.SearchAfter)}");
            if (!String.IsNullOrWhiteSpace(request.SortField)) sb.Append($"&sortField={Esc(request.SortField)}");
            if (request.SortReverse) sb.Append("&sortReverse=true");
            if (!String.IsNullOrWhiteSpace(request.Filter)) sb.Append($"&filter={Esc(request.Filter)}");
            return sb.ToString();
        }

        #endregion

        #region Helpers

        private static string Esc(string value) => Uri.EscapeDataString(value ?? String.Empty);

        private HttpRequestMessage CreateRequest(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, $"{ServerUrl}/{path}");

            if (!String.IsNullOrEmpty(_clientId) && !String.IsNullOrEmpty(_clientSecret))
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }

            return request;
        }

        private async Task<T> SendAsync<T>(HttpMethod method, string path, object body = null, CancellationToken cancellationToken = default)
            where T : IApiResult
        {
            using var request = CreateRequest(method, path);
            if (body != null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return await ReadResultAsync<T>(response).ConfigureAwait(false);
        }

        private static async Task<T> ReadResultAsync<T>(HttpResponseMessage response)
            where T : IApiResult
        {
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string message = null;
                try { message = json.DeserializeJson<ApiErrorResult>()?.Message; } catch { }

                throw new LuceneServerClientException(String.IsNullOrEmpty(message)
                    ? $"API call returns HTTP status code {(int)response.StatusCode} ({response.StatusCode})"
                    : message);
            }

            var result = json.DeserializeJson<T>();
            if (result == null || !result.Success)
            {
                string message = null;
                try { message = json.DeserializeJson<ApiErrorResult>()?.Message; } catch { }
                throw new LuceneServerClientException(message ?? "Unknown error");
            }

            return result;
        }

        #endregion
    }

    public class SearchRequest
    {
        public string Query { get; set; }
        public bool Phonetic { get; set; }
        public string OutFields { get; set; }
        public int From { get; set; }
        public int Size { get; set; } = 20;
        public string SearchAfter { get; set; }
        public string SortField { get; set; }
        public bool SortReverse { get; set; }
        public string Filter { get; set; }
    }
}
