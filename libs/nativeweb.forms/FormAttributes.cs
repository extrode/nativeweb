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
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
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
