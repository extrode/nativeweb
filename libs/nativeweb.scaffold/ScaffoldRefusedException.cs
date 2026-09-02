namespace NativeWeb.Scaffold;

/// <summary>
/// Thrown by a store's <see cref="IScaffoldStore{T}.CreateAsync"/> or
/// <see cref="IScaffoldStore{T}.UpdateAsync"/> to refuse a save with a message the
/// scaffolded form shows.
/// </summary>
/// <remarks>
/// <para>
/// The generated create and update handlers catch this one type and re-render the
/// form with <see cref="Exception.Message"/> in the same place a "Title is
/// required." message goes, the submitted values still in their inputs. It is
/// how a store says what only the store can know: a value that must be unique
/// and is not, a slug the address bar cannot carry, a state the row cannot move
/// to. The generated form validates shape; the store validates meaning.
/// </para>
/// <para>
/// Anything else a store throws is still an error. This is not a catch-all for
/// database failures, and a store that wraps every exception in it turns a broken
/// connection into a form message.
/// </para>
/// </remarks>
public sealed class ScaffoldRefusedException(string message) : Exception(message)
{
}
