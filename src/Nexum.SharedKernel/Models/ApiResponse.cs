namespace Nexum.SharedKernel.Models;

public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public ApiError? Error { get; init; }
    public ApiMeta Meta { get; init; } = new();

    public static ApiResponse<T> Ok(T data) => new() { Success = true, Data = data };
    public static ApiResponse<T> Fail(string code, string message, object? details = null) =>
        new() { Success = false, Error = new ApiError(code, message, details) };
}

public sealed record ApiError(string Code, string Message, object? Details = null);

public sealed class ApiMeta
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string RequestId { get; init; } = Guid.NewGuid().ToString();
}

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
