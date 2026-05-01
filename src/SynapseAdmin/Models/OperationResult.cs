using MudBlazor;

namespace SynapseAdmin.Models;

public class OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Severity Severity { get; set; } = Severity.Info;

    public static OperationResult Ok(string message = "") => new() { Success = true, Message = message, Severity = Severity.Success };
    public static OperationResult Failure(string message, Severity severity = Severity.Error) => new() { Success = false, Message = message, Severity = severity };
    
    /// <summary>
    /// Returns a failure result with Severity.Normal, which indicates a silent failure (e.g., cancellation).
    /// UI components should check for this to avoid showing error messages.
    /// </summary>
    public static OperationResult Cancelled() => new() { Success = false, Message = "Operation cancelled", Severity = Severity.Normal };
}

public class OperationResult<T> : OperationResult
{
    public T? Data { get; set; }

    public static OperationResult<T> Ok(T data, string message = "") => new() { Success = true, Data = data, Message = message, Severity = Severity.Success };
    public new static OperationResult<T> Failure(string message, Severity severity = Severity.Error) => new() { Success = false, Message = message, Severity = severity };
    
    /// <summary>
    /// Returns a failure result with Severity.Normal, which indicates a silent failure (e.g., cancellation).
    /// </summary>
    public new static OperationResult<T> Cancelled() => new() { Success = false, Message = "Operation cancelled", Severity = Severity.Normal };
}
