namespace SynapseAdmin.Models;

public class OperationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public static OperationResult Ok() => new() { Success = true };
    public static OperationResult Failure(string message) => new() { Success = false, ErrorMessage = message };
}

public class OperationResult<T> : OperationResult
{
    public T? Data { get; set; }

    public static OperationResult<T> Ok(T data) => new() { Success = true, Data = data };
    public new static OperationResult<T> Failure(string message) => new() { Success = false, ErrorMessage = message };
}
