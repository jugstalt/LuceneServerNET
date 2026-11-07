using LuceneServerNET.Client.Exceptions;
using LuceneServerNET.Core.Extensions;
using LuceneServerNET.Core.Models.Result;
using System;
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
            var resultJson = await httpResponse.Content.ReadAsStringAsync();

            if (httpResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                string message = null;
                try
                {
                    message = resultJson.DeserializeJson<ApiErrorResult>()?.Message;
                }
                catch { /* body is not an ApiErrorResult */ }

                throw new LuceneServerClientException(String.IsNullOrEmpty(message)
                    ? $"API call returns HTTP status code { httpResponse.StatusCode }"
                    : $"API call returns HTTP status code { httpResponse.StatusCode }: { message }");
            }

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
