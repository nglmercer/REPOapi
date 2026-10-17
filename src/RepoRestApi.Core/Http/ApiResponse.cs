using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using RepoRestApi.Core.Models;
namespace RepoRestApi.Core.Http;
public sealed class ApiResponse
{
    public int Status { get; }
    public string Json { get; }
    public ApiResponse(int status, object body) { Status = status; Json = JsonConvert.SerializeObject(body, Settings); }
    private ApiResponse(string json) { Status = 200; Json = json; }
    public static ApiResponse Raw(string json) => new ApiResponse(json);
    public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver(), MaxDepth = 16 };
    public static ApiResponse Error(ApiException e) => new ApiResponse(e.Status, new { success = false, error = new { code = e.Code, message = e.Message, details = e.Details } });
}
