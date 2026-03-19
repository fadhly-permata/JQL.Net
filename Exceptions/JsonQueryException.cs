namespace JQL.Net.Exceptions;

/// <summary>
///     An exception that is thrown when a SQL-like query is invalid.
/// </summary>
public class JsonQueryException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="JsonQueryException" /> class.
    /// </summary>
    /// <param name="message">
    ///     The message that describes the error.
    /// </param>
    public JsonQueryException(string message)
        : base(message) { }

    /// <summary>
    ///     Initializes a new instance of the <see cref="JsonQueryException" /> class.
    /// </summary>
    /// <param name="message">
    ///     The message that describes the error.
    /// </param>
    /// <param name="innerException">
    ///     The exception that is the cause of the current exception.
    /// </param>
    public JsonQueryException(string message, Exception innerException)
        : base(message, innerException) { }
}
