namespace NativeWeb.Forms;

/// <summary>
/// Marks a partial record as a form model, so the generator emits its binder.
/// </summary>
/// <remarks>
/// <para>
/// The declaration is the contract. A field the record does not declare is not
/// read, so it cannot reach a handler — Rails' strong parameters, except enforced
/// by the compiler rather than by a runtime filter someone has to remember to
/// apply. That is spec 014 R4, and it is the half of this feature that is about
/// correctness rather than typing less.
/// </para>
/// <para>
/// <b>Why a record, and why partial.</b> The generator emits
/// <c>IBindableFromHttpContext&lt;TSelf&gt;</c> onto the same type, which needs a
/// partial declaration to extend. A record because binding produces a value and
/// the handler should not be able to mutate it back.
/// </para>
/// <para>
/// <b>Why this exists at all rather than <c>[FromForm]</c> on a complex type.</b>
/// Measured, brainstorm 001 §12: complex-type <c>[FromForm]</c> is not
/// NativeAOT-compatible — the Request Delegate Generator cannot statically resolve
/// it (RDG003), falls back to reflection, and trips IL2026/IL3050, which under this
/// repo's <c>TreatWarningsAsErrors</c> is a failed build. That is upstream and
/// deliberate. <c>[AsParameters]</c> compiles but binds every form field to
/// <see langword="null"/> in silence. The only shape that both binds and stays
/// AOT-clean is a static <c>BindAsync</c>, and writing one by hand drops two guards
/// that fail silently — which is what the generator exists to stop.
/// </para>
/// </remarks>
// Class only, and not by preference: IBindableFromHttpContext<TSelf> constrains
// TSelf to a reference type, so a struct form model could not implement the binder
// the generator emits.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class FormAttribute : Attribute
{
}

/// <summary>
/// The wire name of a form field, when it differs from the member name.
/// </summary>
/// <remarks>
/// Without this the generator uses the member name with its first character
/// lowercased, which matches what the existing hand-written endpoints already post
/// (<c>firstName</c>, <c>confirmPassphrase</c>). This attribute is for the cases
/// that convention cannot reach — a field named by an external system, or one whose
/// wire name is not a legal C# identifier.
/// </remarks>
/// <param name="name">The name as it appears in the posted form.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class FormFieldAttribute(string name) : Attribute
{
    /// <summary>The name as it appears in the posted form.</summary>
    public string Name { get; } = name;
}

/// <summary>
/// The limits a file-valued form field is bound under.
/// </summary>
/// <remarks>
/// <para>
/// Required on every <see cref="FormFile"/> member, because a public upload with no
/// declared ceiling is a way to fill a disk. NWF006 is the build error for a
/// declaration the binder cannot honour.
/// </para>
/// <para>
/// <b>The two limits are enforced at different moments, and the difference matters.</b>
/// <see cref="MaxBytes"/> is installed as the request's multipart body limit before
/// the form is read, so an oversize request is aborted mid-read and answered 413 --
/// the bytes are never all received. <see cref="Accept"/> can only be checked once
/// the form has been read, because a part's content type is not exposed until then;
/// it is checked before anything is written to storage, and answers 415. Spec 027 R4
/// records why the second is not the first.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class FormFileAttribute : Attribute
{
    /// <summary>
    /// The largest accepted request body, in bytes. Must be greater than zero.
    /// </summary>
    /// <remarks>
    /// This bounds the whole multipart body, not the one part, because that is what
    /// the server can enforce before reading it. A form carrying a 1 MB file and two
    /// short text fields needs a little more than 1 MB here.
    /// </remarks>
    public long MaxBytes { get; set; }

    /// <summary>
    /// The content types accepted, or <see langword="null"/> to accept any.
    /// </summary>
    /// <remarks>
    /// Compared case-insensitively against the client's declared type, with any
    /// parameters after a <c>;</c> ignored, so <c>image/png</c> matches
    /// <c>image/png; charset=binary</c>. An entry that is empty or whitespace is
    /// NWF006: it would silently accept the request that declares no type at all.
    /// </remarks>
    public string[]? Accept { get; set; }
}
