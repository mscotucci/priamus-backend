namespace Priamus.Trips.Operations.Application.Services;

/// <summary>
/// Scoped holder of the values an Italian error message carries (<c>{0}</c>, <c>{1}</c>...).
/// A handler calls <see cref="Set"/> immediately before returning the error; the problem-details
/// localization step formats the message with them. <c>Error.Description</c> stays the English text
/// without values.
/// </summary>
public sealed class ErrorMessageArguments
{
    /// <summary>The values to format the message with; empty until <see cref="Set"/> is called.</summary>
    public IReadOnlyList<object> Values { get; private set; } = [];

    /// <summary>Stores the values for the error about to be returned.</summary>
    /// <param name="values">The format arguments, in the order of the placeholders.</param>
    public void Set(params object[] values) => Values = values;
}
