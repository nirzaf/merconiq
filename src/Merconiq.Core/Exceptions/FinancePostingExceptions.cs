namespace Merconiq.Core.Exceptions;

/// <summary>
/// A finance posting request violated a kernel invariant: unbalanced lines, an invalid account,
/// a closed or missing fiscal period, or a currency mismatch. Nothing was committed.
/// </summary>
public sealed class FinancePostingValidationException(string message) : Exception(message);

/// <summary>
/// A finance state transition conflicts with committed history: a committed source action was
/// replayed with a different payload, or a journal state transition is not allowed. Nothing
/// was committed by the failing attempt.
/// </summary>
public sealed class FinancePostingConflictException(string message) : Exception(message);
