namespace JobScreener.Core.Sources;

/// <summary>Raised when a source refuses us (HTTP 429, challenge page). Never retried in a loop.</summary>
public sealed class SourceBlockedException(string message) : Exception(message);
