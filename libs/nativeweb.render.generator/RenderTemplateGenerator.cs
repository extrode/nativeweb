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
        // PhysicalPath rides alongside the logical one purely so diagnostics can
        // name a file an editor can open: everything else here addresses
        // templates logically, and LogicalPath throws the real path away.
        IncrementalValueProvider<ImmutableArray<(string Path, string PhysicalPath, string Source)>> templates =
            context.AdditionalTextsProvider
                .Where(static text =>
                    text.Path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                    && LogicalPath(text.Path) is not null)
                .Select(static (text, cancellationToken) =>
                    (LogicalPath(text.Path)!, text.Path, text.GetText(cancellationToken)?.ToString() ?? ""))
                .Collect();

        IncrementalValueProvider<ImmutableArray<(string Id, string Source)>> locales =
            context.AdditionalTextsProvider
                .Where(static text =>
                    text.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    && LocaleId(text.Path) is not null)
                .Select(static (text, cancellationToken) =>
                    (LocaleId(text.Path)!, text.GetText(cancellationToken)?.ToString() ?? ""))
                .Collect();

        IncrementalValueProvider<string> rootNamespace = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
                options.GlobalOptions.TryGetValue("build_property.RootNamespace", out string? value) && value.Length > 0
                    ? value
                    : "NativeWeb");

        context.RegisterSourceOutput(templates.Combine(locales).Combine(rootNamespace), static (productionContext, input) =>
        {
            Dictionary<string, TemplateModel> models = new(StringComparer.Ordinal);
            foreach ((string path, string physicalPath, string source) in input.Left.Left.OrderBy(p => p.Path, StringComparer.Ordinal))
            {
                // Same logical path twice (e.g. two projects' AdditionalFiles merged)
                // falls out as NWT009 via class-name collision; last one wins here.
                models[path] = TemplateModel.Parse(path, physicalPath, source);
            }

            List<LocaleModel> catalogs = [.. input.Left.Right
                .OrderBy(l => l.Id, StringComparer.Ordinal)
                .Select(l => LocaleModel.Parse(l.Id, l.Source))];

            TemplateEmitter.Emit(productionContext, models, catalogs, input.Right);
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

    /// <summary>
    /// Maps a locales/*.json path to its lowercase locale id ("en", "en-us").
    /// Files not directly under a locales/ directory are ignored (null).
    /// </summary>
    internal static string? LocaleId(string physicalPath)
    {
        string normalized = physicalPath.Replace('\\', '/');
        int marker = normalized.LastIndexOf("/locales/", StringComparison.OrdinalIgnoreCase);
        int start;
        if (marker >= 0)
        {
            start = marker + "/locales/".Length;
        }
        else if (normalized.StartsWith("locales/", StringComparison.OrdinalIgnoreCase))
        {
            start = "locales/".Length;
        }
        else
        {
            return null;
        }

        string relative = normalized.Substring(start);
        if (relative.IndexOf('/') >= 0)
        {
            return null;
        }

        return relative.Substring(0, relative.Length - ".json".Length).ToLowerInvariant();
    }
}
