namespace NexusAI.Api.Models;

/// <summary>統一 API 回應格式</summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }

    public static ApiResponse<T> Ok(T data) =>
        new() { Success = true, Data = data };

    public static ApiResponse<T> Fail(string error) =>
        new() { Success = false, Error = error };
}

public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse Ok() =>
        new() { Success = true };
    public new static ApiResponse Fail(string error) =>
        new() { Success = false, Error = error };
}
