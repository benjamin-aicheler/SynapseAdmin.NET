using System.Runtime.CompilerServices;

namespace SynapseAdmin.Models;

/// <summary>
/// Thrown by a gateway for an operation it can't carry out with Synapse's result on its homeserver (ADR 0006): the
/// gateway refuses it instead of running it with different semantics. <c>RunGatewayCallAsync</c> reports it as the
/// localized <c>OperationNotSupportedByHomeserver</c> warning. The message names the operation for the log only.
/// </summary>
/// <param name="operation">The refused gateway method; filled in from the caller's name.</param>
public sealed class OperationNotSupportedByHomeserverException([CallerMemberName] string operation = "")
    : NotSupportedException($"The homeserver's gateway doesn't support {operation}.")
{
    /// <summary>The refused gateway method.</summary>
    public string Operation { get; } = operation;
}
