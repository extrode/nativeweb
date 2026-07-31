using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;

namespace NativeWeb.Render.Generator;

/// <summary>
/// Turns parsed templates into generated render classes. Layouts are inlined at
/// generation time (no delegates or closures on the render path); partials become
/// direct method calls; static markup becomes UTF-8 literals (R41).
/// </summary>
internal static class TemplateEmitter
{
    public static void Emit(
        SourceProductionContext context,
        IReadOnlyDictionary<string, TemplateModel> templates,
        IReadOnlyList<LocaleModel> locales,
        string rootNamespace)
    {
        HashSet<string> enKeys = EmitLocales(context, locales, rootNamespace);
        // Surface parse-stage findings first; a template with errors is skipped.
        HashSet<string> broken = [];
        foreach (TemplateModel model in templates.Values)
        {
            foreach (ParseError error in model.Errors)
            {
                switch (error.Kind)
                {
                    case ParseErrorKind.Header:
                        Report(context, model, TemplateDiagnostics.HeaderMalformed, model.Path, error.Message);
                        break;
                    case ParseErrorKind.Expression:
                        Report(context, model, TemplateDiagnostics.ExpressionRejected, model.Path, error.Expression!, error.OffendingChar);
                        break;
                    case ParseErrorKind.Partial:
                        Report(context, model, TemplateDiagnostics.PartialArgMismatch, model.Path, error.Expression!, error.Message);
                        break;
                    default:
                        Report(context, model, TemplateDiagnostics.UnbalancedBlock, model.Path, error.Message);
                        break;
                }

                broken.Add(model.Path);
            }

            foreach (string directive in model.UnknownDirectives)
            {
                Report(context, model, TemplateDiagnostics.UnknownDirective, model.Path, directive);
                broken.Add(model.Path);
            }

            foreach (string param in model.MalformedParams)
            {
                Report(context, model, TemplateDiagnostics.ParamMalformed, model.Path, param);
                broken.Add(model.Path);
            }
        }

        // Duplicate class detection across the whole set.
        Dictionary<string, string> byClassName = new(StringComparer.Ordinal);
        foreach (TemplateModel model in templates.Values.OrderBy(m => m.Path, StringComparer.Ordinal))
        {
            string className = FullClassName(rootNamespace, model.Path);
            if (byClassName.TryGetValue(className, out string? existing))
            {
                Report(context, model, TemplateDiagnostics.DuplicateTemplate, existing, model.Path, className);
                broken.Add(model.Path);
            }
            else
            {
                byClassName.Add(className, model.Path);
            }
        }

        // Every {{t:key}} anywhere (pages, layouts, partials) must exist in the
        // English root catalog (NWT013) — checked on each template's own body so a
        // layout's key is reported once, not per page.
        foreach (TemplateModel model in templates.Values.OrderBy(m => m.Path, StringComparer.Ordinal))
        {
            List<string> keys = [];
            CollectLocKeys(model.Body, keys);
            foreach (string key in keys.Distinct(StringComparer.Ordinal).Where(k => !enKeys.Contains(k)))
            {
                Report(context, model, TemplateDiagnostics.UnknownLocaleKey, model.Path, key);
            }
        }

        // Which templates need the implicit locale parameter (transitively, via
        // layouts and partials).
        HashSet<string> needingLocale = ComputeNeedingLocale(templates);

        // Layouts get no class of their own; a layout that ALSO declares @layout
        // would otherwise vanish silently. Checked before emission so every page
        // using such a layout sees it as broken.
        foreach (TemplateModel model in templates.Values.Where(m => !broken.Contains(m.Path) && IsLayout(m) && m.Layout is not null))
        {
            Report(context, model, TemplateDiagnostics.HeaderMalformed, model.Path,
                "declares @layout but also contains '{{slot …}}' — layout chaining is not supported");
            broken.Add(model.Path);
        }

        foreach (TemplateModel model in templates.Values.OrderBy(m => m.Path, StringComparer.Ordinal))
        {
            if (broken.Contains(model.Path) || IsLayout(model))
            {
                continue;
            }

            EmitTemplate(context, templates, model, rootNamespace, broken, needingLocale);
        }
    }

    /// <summary>
    /// Validates the locale catalogs, resolves fallthrough (dialect → base
    /// language → en) into complete per-locale tables, and emits the Locales
    /// class. Returns the English root's key set for NWT013 checks.
    /// </summary>
    private static HashSet<string> EmitLocales(SourceProductionContext context, IReadOnlyList<LocaleModel> locales, string rootNamespace)
    {
        HashSet<string> enKeys = new(StringComparer.Ordinal);
        if (locales.Count == 0)
        {
            return enKeys;
        }

        bool anyBroken = false;
        foreach (LocaleModel locale in locales)
        {
            foreach (string error in locale.Errors)
            {
                context.ReportDiagnostic(Diagnostic.Create(TemplateDiagnostics.LocaleMalformed, Location.None, locale.Id, error));
                anyBroken = true;
            }
        }

        Dictionary<string, LocaleModel> byId = new(StringComparer.Ordinal);
        foreach (LocaleModel locale in locales)
        {
            if (byId.ContainsKey(locale.Id))
            {
                context.ReportDiagnostic(Diagnostic.Create(TemplateDiagnostics.LocaleMalformed, Location.None, locale.Id, "two locale files share this id"));
                anyBroken = true;
            }
            else
            {
                byId.Add(locale.Id, locale);
            }
        }

        if (!byId.TryGetValue("en", out LocaleModel? en))
        {
            context.ReportDiagnostic(Diagnostic.Create(TemplateDiagnostics.LocaleMalformed, Location.None, "en", "the root catalog locales/en.json is missing — English is the fallthrough for every locale"));
            return enKeys;
        }

        foreach (string key in en.Strings.Keys)
        {
            enKeys.Add(key);
        }

        foreach (LocaleModel locale in locales.Where(l => l.Id != "en").OrderBy(l => l.Id, StringComparer.Ordinal))
        {
            foreach (string key in locale.Strings.Keys.Where(k => !enKeys.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(TemplateDiagnostics.OrphanLocaleKey, Location.None, locale.Id, key));
                anyBroken = true;
            }
        }

        if (anyBroken)
        {
            return enKeys;
        }

        StringBuilder code = new();
        code.AppendLine("// <auto-generated/>");
        code.AppendLine("// Generated from locales/*.json — edit the catalogs, not this file.");
        code.AppendLine("#nullable enable");
        code.AppendLine();
        code.AppendLine($"namespace {rootNamespace};");
        code.AppendLine();
        code.AppendLine("public static class Locales");
        code.AppendLine("{");

        List<LocaleModel> ordered = [.. locales.OrderBy(l => l.Id, StringComparer.Ordinal)];
        foreach (LocaleModel locale in ordered)
        {
            // Build-time fallthrough: en root, then base language, then the
            // dialect's own overrides.
            Dictionary<string, string> table = new(en.Strings, StringComparer.Ordinal);
            LocaleModel? baseModel = null;
            if (locale.BaseLanguage != locale.Id && byId.TryGetValue(locale.BaseLanguage, out baseModel))
            {
                foreach (KeyValuePair<string, string> entry in baseModel.Strings)
                {
                    table[entry.Key] = entry.Value;
                }
            }

            foreach (KeyValuePair<string, string> entry in locale.Strings)
            {
                table[entry.Key] = entry.Value;
            }

            string dir = locale.DirOverride ?? baseModel?.Dir ?? locale.Dir;
            code.AppendLine($"    public static readonly global::NativeWeb.Render.LocaleCatalog {Pascal(locale.Id)} = new({Quote(locale.Id)}, {Quote(dir)}, new global::System.Collections.Generic.KeyValuePair<string, string>[]");
            code.AppendLine("    {");
            foreach (KeyValuePair<string, string> entry in table.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                code.AppendLine($"        new({Quote(entry.Key)}, {Quote(entry.Value)}),");
            }

            code.AppendLine("    });");
            code.AppendLine();
        }

        code.AppendLine("    /// <summary>Resolves a lowercase locale id; anything unknown falls back to English.</summary>");
        code.AppendLine("    public static global::NativeWeb.Render.LocaleCatalog Get(string? locale) => locale switch");
        code.AppendLine("    {");
        foreach (LocaleModel locale in ordered.Where(l => l.Id != "en"))
        {
            code.AppendLine($"        {Quote(locale.Id)} => {Pascal(locale.Id)},");
        }

        code.AppendLine("        _ => En,");
        code.AppendLine("    };");
        code.AppendLine();
        code.AppendLine("    /// <summary>Every shipped locale id, for negotiation options.</summary>");
        code.AppendLine($"    public static readonly string[] All = [{string.Join(", ", ordered.Select(l => Quote(l.Id)))}];");
        code.AppendLine("}");

        context.AddSource($"{rootNamespace}.Locales.g.cs", code.ToString());
        return enKeys;
    }

    private static void CollectLocKeys(List<Node> nodes, List<string> into)
    {
        foreach (Node node in nodes)
        {
            switch (node)
            {
                case LocNode loc:
                    into.Add(loc.Key);
                    break;
                case IfNode i:
                    CollectLocKeys(i.Then, into);
                    CollectLocKeys(i.Else, into);
                    break;
                case ForNode f:
                    CollectLocKeys(f.Body, into);
                    break;
                case FillNode f:
                    CollectLocKeys(f.Body, into);
                    break;
            }
        }
    }

    /// <summary>
    /// The set of template paths whose Render needs the implicit locale
    /// parameter: any template using {{t:}} itself, or (transitively) whose
    /// layout or any called partial needs it.
    /// </summary>
    private static HashSet<string> ComputeNeedingLocale(IReadOnlyDictionary<string, TemplateModel> templates)
    {
        HashSet<string> needing = new(StringComparer.Ordinal);
        foreach (TemplateModel model in templates.Values)
        {
            List<string> keys = [];
            CollectLocKeys(model.Body, keys);
            if (keys.Count > 0)
            {
                needing.Add(model.Path);
            }
        }

        // Propagate to callers until stable (partial/layout graphs are shallow).
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (TemplateModel model in templates.Values.Where(m => !needing.Contains(m.Path)))
            {
                bool needs =
                    (model.Layout is not null && needing.Contains(model.Layout))
                    || CollectPartialPaths(model.Body).Any(needing.Contains);
                if (needs)
                {
                    needing.Add(model.Path);
                    changed = true;
                }
            }
        }

        return needing;
    }

    private static IEnumerable<string> CollectPartialPaths(List<Node> nodes)
    {
        foreach (Node node in nodes)
        {
            switch (node)
            {
                case PartialNode p:
                    yield return p.Path;
                    break;
                case IfNode i:
                    foreach (string path in CollectPartialPaths(i.Then).Concat(CollectPartialPaths(i.Else)))
                    {
                        yield return path;
                    }

                    break;
                case ForNode f:
                    foreach (string path in CollectPartialPaths(f.Body))
                    {
                        yield return path;
                    }

                    break;
                case FillNode f:
                    foreach (string path in CollectPartialPaths(f.Body))
                    {
                        yield return path;
                    }

                    break;
            }
        }
    }

    private static string Quote(string value)
    {
        StringBuilder literal = new(value.Length + 2);
        literal.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"':
                    literal.Append("\\\"");
                    break;
                case '\\':
                    literal.Append("\\\\");
                    break;
                case '\r':
                    literal.Append("\\r");
                    break;
                case '\n':
                    literal.Append("\\n");
                    break;
                case '\t':
                    literal.Append("\\t");
                    break;
                default:
                    if (c is < (char)0x20)
                    {
                        literal.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        literal.Append(c);
                    }

                    break;
            }
        }

        literal.Append('"');
        return literal.ToString();
    }

    private static bool IsLayout(TemplateModel model) => ContainsSlot(model.Body);

    private static bool ContainsSlot(List<Node> nodes) => nodes.Any(n => n switch
    {
        SlotNode => true,
        IfNode i => ContainsSlot(i.Then) || ContainsSlot(i.Else),
        ForNode f => ContainsSlot(f.Body),
        FillNode f => ContainsSlot(f.Body),
        _ => false,
    });

    private static void EmitTemplate(
        SourceProductionContext context,
        IReadOnlyDictionary<string, TemplateModel> templates,
        TemplateModel page,
        string rootNamespace,
        HashSet<string> broken,
        HashSet<string> needingLocale)
    {
        List<TemplateParam> parameters = [.. page.Params];
        List<Node> body = page.Body;

        if (page.Layout is not null)
        {
            if (!templates.TryGetValue(page.Layout, out TemplateModel? layout))
            {
                Report(context, page, TemplateDiagnostics.LayoutNotFound, page.Path, page.Layout);
                return;
            }

            if (broken.Contains(layout.Path))
            {
                return;
            }

            // Merge parameter lists: layout params first, page params after, matching
            // names must agree on type.
            parameters = [.. layout.Params];
            foreach (TemplateParam param in page.Params)
            {
                TemplateParam? existing = parameters.FirstOrDefault(p => p.Name == param.Name);
                if (existing is null)
                {
                    parameters.Add(param);
                }
                else if (existing.Type != param.Type)
                {
                    Report(context, page, TemplateDiagnostics.ParamConflict, page.Path, param.Name, param.Type, existing.Type, layout.Path);
                    return;
                }
            }

            // Split the page body into the implicit Main content and named fills.
            List<Node> main = [];
            Dictionary<string, FillNode> fills = new(StringComparer.Ordinal);
            foreach (Node node in page.Body)
            {
                if (node is FillNode fill)
                {
                    if (fills.ContainsKey(fill.Name))
                    {
                        Report(context, page, TemplateDiagnostics.SlotMisused, page.Path, $"'{{{{fill {fill.Name}}}}}' appears more than once");
                        return;
                    }

                    fills.Add(fill.Name, fill);
                }
                else
                {
                    main.Add(node);
                }
            }

            // A fill inside {{if}}/{{for}} would otherwise be silently dropped.
            if (ContainsFill(main) || fills.Values.Any(f => ContainsFill(f.Body)))
            {
                Report(context, page, TemplateDiagnostics.SlotMisused, page.Path, "'{{fill …}}' must be at the top level of the template");
                return;
            }

            // An explicit {{fill Main}} is allowed instead of implicit content, not
            // alongside it.
            if (fills.TryGetValue("Main", out FillNode? mainFill))
            {
                if (main.Any(n => n is not StaticNode s || s.Text.Trim().Length > 0))
                {
                    Report(context, page, TemplateDiagnostics.SlotMisused, page.Path, "has both '{{fill Main}}' and top-level content — use one or the other");
                    return;
                }

                main = mainFill.Body;
                fills.Remove("Main");
            }

            List<string> layoutSlots = [];
            CollectSlots(layout.Body, layoutSlots);
            if (!layoutSlots.Contains("Main"))
            {
                Report(context, page, TemplateDiagnostics.SlotMisused, page.Path, $"layout '{layout.Path}' does not declare '{{{{slot Main}}}}' — page content would be dropped");
                return;
            }
            foreach (string filled in fills.Keys.Where(f => !layoutSlots.Contains(f)))
            {
                Report(context, page, TemplateDiagnostics.SlotMisused, page.Path, $"'{{{{fill {filled}}}}}' has no matching slot in layout '{layout.Path}'");
                return;
            }

            bool ok = true;
            body = InlineSlots(layout.Body, main, fills, slotName =>
            {
                Report(context, page, TemplateDiagnostics.RequiredSlotUnfilled, page.Path, layout.Path, slotName);
                ok = false;
            });
            if (!ok)
            {
                return;
            }
        }
        else if (ContainsFill(page.Body))
        {
            Report(context, page, TemplateDiagnostics.SlotMisused, page.Path, "'{{fill …}}' requires an '@layout' directive");
            return;
        }

        (string ns, string className) = SplitClassName(rootNamespace, page.Path);

        StringBuilder code = new();
        code.AppendLine("// <auto-generated/>");
        code.AppendLine($"// Generated from templates/{page.Path}.html — edit the template, not this file.");
        code.AppendLine("#nullable enable");
        code.AppendLine("using NativeWeb.Render;");
        code.AppendLine();
        code.AppendLine($"namespace {ns};");
        code.AppendLine();
        code.AppendLine($"public static class {className}");
        code.AppendLine("{");

        // Templates touching {{t:}} (directly or via layout/partials) take the
        // resolved locale catalog as an implicit first parameter.
        List<string> parameterDeclarations = [];
        List<string> argumentNames = [];
        if (needingLocale.Contains(page.Path))
        {
            parameterDeclarations.Add("LocaleCatalog locale");
            argumentNames.Add("locale");
        }

        parameterDeclarations.AddRange(parameters.Select(p => $"{p.Type} {p.Name}"));
        argumentNames.AddRange(parameters.Select(p => p.Name));

        string signature = string.Join(", ", parameterDeclarations);
        string renderParams = parameterDeclarations.Count == 0 ? "" : ", " + signature;

        code.AppendLine($"    public static void Render(ref TemplateWriter writer{renderParams})");
        code.AppendLine("    {");
        EmitNodes(context, templates, page, body, code, indent: 2, broken, rootNamespace, needingLocale);
        code.AppendLine("    }");
        code.AppendLine();
        string trailingArguments = argumentNames.Count == 0 ? "" : ", " + string.Join(", ", argumentNames);
        code.AppendLine($"    public static string ToHtmlString({signature}) =>");
        code.AppendLine($"        TemplateResults.ToHtmlString((ref TemplateWriter writer) => Render(ref writer{trailingArguments}));");
        code.AppendLine();
        code.AppendLine($"    public static Microsoft.AspNetCore.Http.IResult Result({signature}) =>");
        code.AppendLine($"        TemplateResults.Html((ref TemplateWriter writer) => Render(ref writer{trailingArguments}));");
        code.AppendLine("}");

        context.AddSource($"{ns}.{className}.g.cs", code.ToString());
    }

    private static bool ContainsFill(List<Node> nodes) => nodes.Any(n => n switch
    {
        FillNode => true,
        IfNode i => ContainsFill(i.Then) || ContainsFill(i.Else),
        ForNode f => ContainsFill(f.Body),
        _ => false,
    });

    private static void CollectSlots(List<Node> nodes, List<string> into)
    {
        foreach (Node node in nodes)
        {
            switch (node)
            {
                case SlotNode slot:
                    into.Add(slot.Name);
                    break;
                case IfNode i:
                    CollectSlots(i.Then, into);
                    CollectSlots(i.Else, into);
                    break;
                case ForNode f:
                    CollectSlots(f.Body, into);
                    break;
                case FillNode f:
                    // Keep this traversal consistent with ContainsSlot.
                    CollectSlots(f.Body, into);
                    break;
            }
        }
    }

    private static List<Node> InlineSlots(
        List<Node> layoutNodes,
        List<Node> main,
        Dictionary<string, FillNode> fills,
        Action<string> onMissingRequired)
    {
        List<Node> result = [];
        foreach (Node node in layoutNodes)
        {
            switch (node)
            {
                case SlotNode slot when slot.Name == "Main":
                    result.AddRange(main);
                    break;
                case SlotNode slot when fills.TryGetValue(slot.Name, out FillNode? fill):
                    result.AddRange(fill.Body);
                    break;
                case SlotNode slot when slot.Optional:
                    break;
                case SlotNode slot:
                    onMissingRequired(slot.Name);
                    break;
                case IfNode i:
                    IfNode inlinedIf = new(i.Condition) { InElse = false };
                    inlinedIf.Then.AddRange(InlineSlots(i.Then, main, fills, onMissingRequired));
                    inlinedIf.Else.AddRange(InlineSlots(i.Else, main, fills, onMissingRequired));
                    result.Add(inlinedIf);
                    break;
                case ForNode f:
                    ForNode inlinedFor = new(f.Variable, f.Expression);
                    inlinedFor.Body.AddRange(InlineSlots(f.Body, main, fills, onMissingRequired));
                    result.Add(inlinedFor);
                    break;
                default:
                    result.Add(node);
                    break;
            }
        }

        return result;
    }

    private static void EmitNodes(
        SourceProductionContext context,
        IReadOnlyDictionary<string, TemplateModel> templates,
        TemplateModel page,
        List<Node> nodes,
        StringBuilder code,
        int indent,
        HashSet<string> broken,
        string rootNamespace,
        HashSet<string> needingLocale)
    {
        string pad = new(' ', indent * 4);
        foreach (Node node in nodes)
        {
            switch (node)
            {
                case StaticNode s when s.Text.Length > 0:
                    code.AppendLine($"{pad}writer.WriteRaw({Utf8Literal(s.Text)});");
                    break;
                case StaticNode:
                    break;
                case ExprNode e:
                    code.AppendLine($"{pad}writer.Write({e.Expression});");
                    break;
                case LocNode loc:
                    code.AppendLine($"{pad}writer.Write(locale[{Quote(loc.Key)}]);");
                    break;
                case IfNode i:
                    code.AppendLine($"{pad}if ({i.Condition})");
                    code.AppendLine($"{pad}{{");
                    EmitNodes(context, templates, page, i.Then, code, indent + 1, broken, rootNamespace, needingLocale);
                    code.AppendLine($"{pad}}}");
                    if (i.Else.Count > 0)
                    {
                        code.AppendLine($"{pad}else");
                        code.AppendLine($"{pad}{{");
                        EmitNodes(context, templates, page, i.Else, code, indent + 1, broken, rootNamespace, needingLocale);
                        code.AppendLine($"{pad}}}");
                    }

                    break;
                case ForNode f:
                    code.AppendLine($"{pad}foreach (var {f.Variable} in {f.Expression})");
                    code.AppendLine($"{pad}{{");
                    EmitNodes(context, templates, page, f.Body, code, indent + 1, broken, rootNamespace, needingLocale);
                    code.AppendLine($"{pad}}}");
                    break;
                case PartialNode p:
                    EmitPartialCall(context, templates, page, p, code, pad, broken, rootNamespace, needingLocale);
                    break;
                case SlotNode:
                case FillNode:
                    // Unreachable after inlining/validation; defensive no-op.
                    break;
            }
        }
    }

    private static void EmitPartialCall(
        SourceProductionContext context,
        IReadOnlyDictionary<string, TemplateModel> templates,
        TemplateModel page,
        PartialNode partial,
        StringBuilder code,
        string pad,
        HashSet<string> broken,
        string rootNamespace,
        HashSet<string> needingLocale)
    {
        if (!templates.TryGetValue(partial.Path, out TemplateModel? target))
        {
            Report(context, page, TemplateDiagnostics.PartialNotFound, page.Path, partial.Path);
            return;
        }

        if (broken.Contains(target.Path))
        {
            return;
        }

        if (IsLayout(target))
        {
            Report(context, page, TemplateDiagnostics.PartialArgMismatch, page.Path, partial.Path, "declares slots (it is a layout) and cannot be called as a partial");
            return;
        }

        List<string> problems = [];
        foreach (TemplateParam param in target.Params.Where(param => partial.Args.All(a => a.Key != param.Name)))
        {
            problems.Add($"is missing required argument '{param.Name}'");
        }

        foreach (KeyValuePair<string, string> arg in partial.Args.Where(arg => target.Params.All(p => p.Name != arg.Key)))
        {
            problems.Add($"has no parameter named '{arg.Key}'");
        }

        if (problems.Count > 0)
        {
            Report(context, page, TemplateDiagnostics.PartialArgMismatch, page.Path, partial.Path, string.Join("; ", problems));
            return;
        }

        (string ns, string className) = SplitClassName(rootNamespace, target.Path);
        List<string> callArguments = [];
        if (needingLocale.Contains(target.Path))
        {
            callArguments.Add("locale");
        }

        callArguments.AddRange(target.Params.Select(p =>
            $"{p.Name}: {partial.Args.First(a => a.Key == p.Name).Value}"));
        string arguments = callArguments.Count == 0 ? "" : ", " + string.Join(", ", callArguments);
        code.AppendLine($"{pad}global::{ns}.{className}.Render(ref writer{arguments});");
    }

    internal static string FullClassName(string rootNamespace, string path)
    {
        (string ns, string className) = SplitClassName(rootNamespace, path);
        return $"{ns}.{className}";
    }

    internal static (string Namespace, string ClassName) SplitClassName(string rootNamespace, string path)
    {
        string[] segments = path.Split('/');
        IEnumerable<string> namespaceParts = segments.Take(segments.Length - 1).Select(Pascal);
        string ns = string.Join(".", new[] { rootNamespace, "Templates" }.Concat(namespaceParts));
        return (ns, Pascal(segments[segments.Length - 1]));
    }

    private static string Pascal(string segment)
    {
        StringBuilder result = new();
        foreach (string part in segment.Split('-', '_', '.'))
        {
            if (part.Length > 0)
            {
                result.Append(char.ToUpperInvariant(part[0]));
                result.Append(part, 1, part.Length - 1);
            }
        }

        string name = result.Length == 0 ? "Template" : result.ToString();
        return char.IsDigit(name[0]) ? "_" + name : name;
    }

    private static string Utf8Literal(string text)
    {
        StringBuilder literal = new(text.Length + 16);
        literal.Append('"');
        foreach (char c in text)
        {
            switch (c)
            {
                case '"':
                    literal.Append("\\\"");
                    break;
                case '\\':
                    literal.Append("\\\\");
                    break;
                case '\r':
                    literal.Append("\\r");
                    break;
                case '\n':
                    literal.Append("\\n");
                    break;
                case '\t':
                    literal.Append("\\t");
                    break;
                default:
                    if (c is < (char)0x20 or > (char)0x7e)
                    {
                        literal.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        literal.Append(c);
                    }

                    break;
            }
        }

        literal.Append("\"u8");
        return literal.ToString();
    }

    private static void Report(SourceProductionContext context, TemplateModel model, DiagnosticDescriptor descriptor, params object[] args)
    {
        _ = model;
        context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, args));
    }
}
