using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace NativeWeb.Forms.Generator;

/// <summary>
/// Makes binding a form model on a plain route a build error (spec 014 R7).
/// </summary>
/// <remarks>
/// <para>
/// This is the half of 014 that keeps the feature from being net-negative, and it
/// closes two failures that are both silent:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>The bind does not happen at all.</b> Measured 2026-08-30: the Request Delegate
/// Generator runs in the same generation pass as the form generator and cannot see
/// the binder it emits, so it records <c>hasBindAsync: false</c> and treats the model
/// as a JSON body. A form POST to such a route is answered <b>415 by routing</b>
/// before any handler runs — and a JSON POST would bind whatever the client sent.
/// </description></item>
/// <item><description>
/// <b>No antiforgery metadata.</b> §12 measured a route in this shape answering
/// <b>200 OK to a POST with no token</b>, because <c>UseAntiforgery()</c> looks for
/// endpoint metadata that binding does not attach.
/// </description></item>
/// </list>
/// <para>
/// <c>MapFormPost</c> closes both by construction, so the diagnostic has one fix and
/// no escape hatch. It reports at the <c>Map*</c> call rather than at the model,
/// because the same model may be bound correctly by one route and wrongly by another.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingAntiforgeryAnalyzer : DiagnosticAnalyzer
{
    // Every verb, not only the unsafe ones. The antiforgery half of the failure is
    // specific to a mutating request, but the "bound as a JSON body" half is not:
    // a [Form] model on a MapGet is broken too, just differently.
    private static readonly ImmutableHashSet<string> MapMethods = ImmutableHashSet.Create(
        "MapGet", "MapPost", "MapPut", "MapPatch", "MapDelete", "MapMethods", "Map", "MapFallback");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(FormDiagnostics.MissingAntiforgery);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(Analyze, OperationKind.Invocation);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;

        if (!MapMethods.Contains(invocation.TargetMethod.Name))
        {
            return;
        }

        INamedTypeSymbol? form = BoundFormModel(invocation);
        if (form is null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            FormDiagnostics.MissingAntiforgery,
            invocation.Syntax.GetLocation(),
            RouteName(invocation),
            form.ToDisplayString(),
            invocation.TargetMethod.Name));
    }

    // The first parameter of the handler delegate whose type carries [Form].
    private static INamedTypeSymbol? BoundFormModel(IInvocationOperation invocation)
    {
        foreach (IArgumentOperation argument in invocation.Arguments)
        {
            foreach (IParameterSymbol parameter in HandlerParameters(argument.Value))
            {
                if (parameter.Type is INamedTypeSymbol named && HasFormAttribute(named))
                {
                    return named;
                }
            }
        }

        return null;
    }

    private static ImmutableArray<IParameterSymbol> HandlerParameters(IOperation value)
    {
        IOperation current = value;
        while (current is IConversionOperation conversion)
        {
            current = conversion.Operand;
        }

        if (current is IDelegateCreationOperation creation)
        {
            current = creation.Target;
        }

        return current switch
        {
            IAnonymousFunctionOperation lambda => lambda.Symbol.Parameters,
            IMethodReferenceOperation reference => reference.Method.Parameters,
            _ => ImmutableArray<IParameterSymbol>.Empty,
        };
    }

    private static bool HasFormAttribute(INamedTypeSymbol type) =>
        type.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == FormGenerator.FormAttribute);

    // The route pattern if it is a literal, so the message names something the
    // author can find. Falls back to the method name.
    private static string RouteName(IInvocationOperation call)
    {
        foreach (IArgumentOperation argument in call.Arguments)
        {
            IOperation value = argument.Value;
            while (value is IConversionOperation conversion)
            {
                value = conversion.Operand;
            }

            if (value.ConstantValue.HasValue && value.ConstantValue.Value is string literal)
            {
                return literal;
            }
        }

        return call.TargetMethod.Name;
    }
}
