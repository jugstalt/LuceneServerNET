using LuceneServerNET.Client.Exceptions;
using LuceneServerNET.Core.Models.Result;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace LuceneServerNET.Client.Extensions
{
    static class HttpClientExtensions
    {
        async public static Task<T> DeserializeFromSuccessResponse<T>(this HttpResponseMessage httpResponse)
            where T : IApiResult
        {
            if (httpResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new LuceneServerClientException($"API call returns HTTP status code { httpResponse.StatusCode }");
            }

            var resultJson = await httpResponse.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions();
            options.PropertyNameCaseInsensitive = true;
            options.Converters.Add(new JsonStringEnumConverter());

            var apiResult = JsonSerializer.Deserialize<T>(resultJson, options);
            if (apiResult.Success == false)
            {
                var errorResult = JsonSerializer.Deserialize<ApiErrorResult>(resultJson, options);
                throw new LuceneServerClientException(errorResult.Message ?? "Unknown error");
            }

            return apiResult;
        }
    }
}
