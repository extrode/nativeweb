using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace NativeWeb.Forms.Generator;

/// <summary>
/// Makes forgetting the antiforgery metadata a build error (spec 014 R7).
/// </summary>
/// <remarks>
/// <para>
/// This is the half of 014 that keeps the feature from being net-negative. §12
/// measured that a route bound through <c>IBindableFromHttpContext</c> compiles
/// clean, binds every field, and answers <b>200 OK to a POST with no antiforgery
/// token</b>, because <c>UseAntiforgery()</c> looks for endpoint metadata the bind
/// does not attach. Making binding ergonomic without closing that would ship a
/// foot-gun with better ergonomics.
/// </para>
/// <para>
/// It reports at the <c>Map*</c> call rather than at the model, because the metadata
/// belongs to the endpoint: the same form model may be bound by two routes and only
/// one of them be wrong.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingAntiforgeryAnalyzer : DiagnosticAnalyzer
{
    private const string RequireFormAntiforgery = "RequireFormAntiforgery";

    // The unsafe verbs. A GET carrying a form model is a different mistake, and
    // reporting it as missing antiforgery would name the wrong problem.
    private static readonly ImmutableHashSet<string> MapMethods = ImmutableHashSet.Create(
        "MapPost", "MapPut", "MapPatch", "MapDelete", "MapMethods");

    // Anything that puts IAntiforgeryMetadata on the endpoint counts, not only this
    // library's extension: a consumer with its own metadata type is not wrong.
    private static readonly ImmutableHashSet<string> MetadataMethods = ImmutableHashSet.Create(
        RequireFormAntiforgery, "WithMetadata", "ValidateAntiforgery");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(FormDiagnostics.MissingAntiforgery);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Block-scoped rather than per-invocation, because the metadata call is not
        // always chained: `var route = app.MapPost(...); route.RequireFormAntiforgery();`
        // is the same endpoint written over two statements, and reporting it would be
        // a wrong build error rather than a caught foot-gun.
        context.RegisterOperationBlockAction(Analyze);
    }

    private static void Analyze(OperationBlockAnalysisContext context)
    {
        var candidates = new List<(IInvocationOperation Call, INamedTypeSymbol Form)>();
        var reassured = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (IOperation block in context.OperationBlocks)
        {
            foreach (IOperation operation in block.Descendants())
            {
                if (operation is not IInvocationOperation invocation)
                {
                    continue;
                }

                if (MetadataMethods.Contains(invocation.TargetMethod.Name))
                {
                    ISymbol? receiver = ReceiverSymbol(invocation);
                    if (receiver is not null)
                    {
                        reassured.Add(receiver);
                    }

                    continue;
                }

                if (!MapMethods.Contains(invocation.TargetMethod.Name))
                {
                    continue;
                }

                INamedTypeSymbol? form = BoundFormModel(invocation);
                if (form is not null)
                {
                    candidates.Add((invocation, form));
                }
            }
        }

        foreach ((IInvocationOperation call, INamedTypeSymbol form) in candidates)
        {
            if (IsChainedToMetadata(call) || AssignedSymbolIsReassured(call, reassured))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                FormDiagnostics.MissingAntiforgery,
                call.Syntax.GetLocation(),
                RouteName(call),
                form.ToDisplayString()));
        }
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

    // `app.MapPost(...).RequireFormAntiforgery()` — the Map call is the instance of
    // an enclosing invocation somewhere up the chain.
    private static bool IsChainedToMetadata(IInvocationOperation call)
    {
        IOperation? current = call.Parent;
        while (current is not null)
        {
            if (current is IInvocationOperation outer)
            {
                if (MetadataMethods.Contains(outer.TargetMethod.Name))
                {
                    return true;
                }

                current = outer.Parent;
                continue;
            }

            // An extension method's receiver is its first ARGUMENT, not its instance,
            // and every one of these extends RouteHandlerBuilder — so the chain walks
            // through an argument node at each link.
            if (current is IConversionOperation or IArgumentOperation)
            {
                current = current.Parent;
                continue;
            }

            return false;
        }

        return false;
    }

    // `RouteHandlerBuilder route = app.MapPost(...);` followed by
    // `route.RequireFormAntiforgery();` anywhere in the same block.
    private static bool AssignedSymbolIsReassured(IInvocationOperation call, HashSet<ISymbol> reassured)
    {
        IOperation? current = call.Parent;
        while (current is IConversionOperation)
        {
            current = current.Parent;
        }

        ISymbol? assigned = current switch
        {
            IVariableInitializerOperation initializer =>
                (initializer.Parent as IVariableDeclaratorOperation)?.Symbol,
            ISimpleAssignmentOperation assignment => TargetSymbol(assignment.Target),
            _ => null,
        };

        return assigned is not null && reassured.Contains(assigned);
    }

    private static ISymbol? ReceiverSymbol(IInvocationOperation invocation)
    {
        IOperation? instance = invocation.Instance;

        // An extension method's receiver arrives as the first argument, not as
        // Instance -- which is every one of these, since they all extend
        // RouteHandlerBuilder.
        if (instance is null && invocation.Arguments.Length > 0)
        {
            instance = invocation.Arguments[0].Value;
        }

        while (instance is IConversionOperation conversion)
        {
            instance = conversion.Operand;
        }

        return instance is null ? null : TargetSymbol(instance);
    }

    private static ISymbol? TargetSymbol(IOperation operation) => operation switch
    {
        ILocalReferenceOperation local => local.Local,
        IFieldReferenceOperation field => field.Field,
        IParameterReferenceOperation parameter => parameter.Parameter,
        IPropertyReferenceOperation property => property.Property,
        _ => null,
    };

    // The route pattern if it is a literal, so the message names something the
    // author can find. Falls back to the method name.
    private static string RouteName(IInvocationOperation call)
    {
        foreach (IArgumentOperation argument in call.Arguments)
        {
            if (argument.Value is IConversionOperation conversion
                && conversion.Operand.ConstantValue.HasValue
                && conversion.Operand.ConstantValue.Value is string viaConversion)
            {
                return viaConversion;
            }

            if (argument.Value.ConstantValue.HasValue
                && argument.Value.ConstantValue.Value is string literal)
            {
                return literal;
            }
        }

        return call.TargetMethod.Name;
    }
}
