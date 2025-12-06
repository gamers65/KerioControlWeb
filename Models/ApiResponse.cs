using Newtonsoft.Json;
using System.Collections.Generic;

namespace KerioControlWeb.Models
{
    public class ApiError
    {
        [JsonProperty("inputIndex")]
        public int InputIndex { get; set; }

        [JsonProperty("code")]
        public string Code { get; set; } = string.Empty;

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;
    }

    public class ApiResult
    {
        [JsonProperty("errors")]
        public List<ApiError> Errors { get; set; } = new List<ApiError>();

        [JsonProperty("result")]
        public List<object> Result { get; set; } = new List<object>();
    }

    public class ApiResponse
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("result")]
        public ApiResult? Result { get; set; }

        [JsonProperty("error")]
        public dynamic? Error { get; set; }
    }
}