namespace SynapseAdmin.Models;

public class OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    public static OperationResult Ok(string message = "") => new() { Success = true, Message = message };
    public static OperationResult Failure(string message) => new() { Success = false, Message = message };
}

public class OperationResult<T> : OperationResult
{
    public T? Data { get; set; }

    public static OperationResult<T> Ok(T data, string message = "") => new() { Success = true, Data = data, Message = message };
    public new static OperationResult<T> Failure(string message) => new() { Success = false, Message = message };
}
