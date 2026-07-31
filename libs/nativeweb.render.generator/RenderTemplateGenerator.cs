using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;

namespace NativeWeb.Render.Generator;

/// <summary>
/// Incremental generator entry point: every AdditionalFile matching
/// templates/**/*.html is parsed to a <see cref="TemplateModel"/>, then the whole
/// set is emitted together (layouts and partials resolve across files).
/// </summary>
[Generator]
public sealed class RenderTemplateGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<(string Path, string Source)>> templates =
            context.AdditionalTextsProvider
                .Where(static text =>
                    text.Path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                    && LogicalPath(text.Path) is not null)
                .Select(static (text, cancellationToken) =>
                    (LogicalPath(text.Path)!, text.GetText(cancellationToken)?.ToString() ?? ""))
                .Collect();

        IncrementalValueProvider<string> rootNamespace = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
                options.GlobalOptions.TryGetValue("build_property.RootNamespace", out string? value) && value.Length > 0
                    ? value
                    : "NativeWeb");

        context.RegisterSourceOutput(templates.Combine(rootNamespace), static (productionContext, input) =>
        {
            Dictionary<string, TemplateModel> models = new(StringComparer.Ordinal);
            foreach ((string path, string source) in input.Left.OrderBy(p => p.Path, StringComparer.Ordinal))
            {
                // Same logical path twice (e.g. two projects' AdditionalFiles merged)
                // falls out as NWT009 via class-name collision; last one wins here.
                models[path] = TemplateModel.Parse(path, source);
            }

            TemplateEmitter.Emit(productionContext, models, input.Right);
        });
    }

    /// <summary>
    /// Maps a physical file path to its logical template path: the part after the
    /// last "templates" directory segment, forward slashes, no extension.
    /// Files not under a templates/ directory are ignored (null).
    /// </summary>
    internal static string? LogicalPath(string physicalPath)
    {
        string normalized = physicalPath.Replace('\\', '/');
        int marker = normalized.LastIndexOf("/templates/", StringComparison.OrdinalIgnoreCase);
        int start;
        if (marker >= 0)
        {
            start = marker + "/templates/".Length;
        }
        else if (normalized.StartsWith("templates/", StringComparison.OrdinalIgnoreCase))
        {
            start = "templates/".Length;
        }
        else
        {
            return null;
        }

        string relative = normalized.Substring(start);
        return relative.Substring(0, relative.Length - ".html".Length);
    }
}
