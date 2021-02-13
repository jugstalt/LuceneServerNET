using LuceneServerNET.Client.Exceptions;
using LuceneServerNET.Core.Extensions;
using LuceneServerNET.Core.Models.Result;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace LuceneServerNET.Client.Extensions
{
    static class HttpClientExtensions
    {
        async public static Task<T> DeserializeFromSuccessResponse<T>(this HttpResponseMessage httpResponse, bool throwExcpeitonIfNotSucceeded = true)
            where T : IApiResult
        {
            if (httpResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new LuceneServerClientException($"API call returns HTTP status code { httpResponse.StatusCode }");
            }

            var resultJson = await httpResponse.Content.ReadAsStringAsync();

            var apiResult = resultJson.DeserializeJson<T>(); 

            if (apiResult.Success == false && throwExcpeitonIfNotSucceeded)
            {
                var errorResult = resultJson.DeserializeJson<ApiErrorResult>();
                throw new LuceneServerClientException(errorResult.Message ?? "Unknown error");
            }

            return apiResult;
        }
    }
}
