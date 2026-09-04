using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace NativeWeb.Forms.Generator;

// Turns a [Form]-decorated partial record into the binder of brainstorm 001 §12's
// variant 5b.
//
// Why a generator rather than the platform's own declarative binding: §12 measured
// that complex-type [FromForm] is not NativeAOT-compatible (RDG003 + IL2026 +
// IL3050, upstream-deliberate) and that [AsParameters] binds every form field to
// null in silence while skipping the content-type check. The only shape that binds
// and stays AOT-clean is a static BindAsync -- and writing one by hand loses the
// 415 and the antiforgery metadata, neither of which fails loudly. This emits the
// first; MissingAntiforgeryAnalyzer makes forgetting the second a build error.
[Generator]
public sealed class FormGenerator : IIncrementalGenerator
{
    internal const string FormAttribute = "NativeWeb.Forms.FormAttribute";
    internal const string FormFieldAttribute = "NativeWeb.Forms.FormFieldAttribute";
    internal const string FormFileAttribute = "NativeWeb.Forms.FormFileAttribute";
    internal const string FormFileType = "NativeWeb.Forms.FormFile";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<Parsed> parsed = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                FormAttribute,
                predicate: static (node, _) => true,
                transform: static (ctx, _) => Parse(ctx));

        context.RegisterSourceOutput(parsed, static (spc, result) => Produce(spc, result));
    }

    // Parsing and validation happen in the transform so the pipeline caches on the
    // result rather than re-running per output; the diagnostics ride along because
    // only RegisterSourceOutput may report them. Parsed and FormModel are structurally
    // equatable for that to mean anything -- see the note on Parsed.
    private static Parsed Parse(GeneratorAttributeSyntaxContext ctx)
    {
        var diagnostics = new List<Diagnostic>();

        if (ctx.TargetSymbol is not INamedTypeSymbol type)
        {
            return new Parsed(null, diagnostics);
        }

        Location location = ctx.TargetNode.GetLocation();
        return new Parsed(FormModel.Build(type, location, diagnostics), diagnostics);
    }

    private static void Produce(SourceProductionContext spc, Parsed result)
    {
        foreach (Diagnostic diagnostic in result.Diagnostics)
        {
            spc.ReportDiagnostic(diagnostic);
        }

        if (result.Model is null)
        {
            return;
        }

        spc.AddSource(
            FormEmitter.HintName(result.Model),
            SourceText.From(FormEmitter.Emit(result.Model), Encoding.UTF8));
    }

    // Equatable, because the pipeline caches on this type and a reference-equality
    // one never compares equal. The diagnostics are compared by their descriptor and
    // location rather than by identity for the same reason.
    private sealed class Parsed : IEquatable<Parsed>
    {
        public Parsed(FormModel? model, List<Diagnostic> diagnostics)
        {
            Model = model;
            Diagnostics = diagnostics;
        }

        public FormModel? Model { get; }

        public List<Diagnostic> Diagnostics { get; }

        public bool Equals(Parsed? other)
        {
            if (other is null
                || !Equals(Model, other.Model)
                || Diagnostics.Count != other.Diagnostics.Count)
            {
                return false;
            }

            for (int i = 0; i < Diagnostics.Count; i++)
            {
                if (!Diagnostics[i].Equals(other.Diagnostics[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as Parsed);

        public override int GetHashCode() => Model?.GetHashCode() ?? Diagnostics.Count;
    }
}
