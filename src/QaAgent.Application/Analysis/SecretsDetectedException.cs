namespace QaAgent.Application.Analysis;

/// <summary>Changed lines look like credentials; the run must stop instead of publishing them.</summary>
public sealed class SecretsDetectedException(string message) : InvalidOperationException(message);
