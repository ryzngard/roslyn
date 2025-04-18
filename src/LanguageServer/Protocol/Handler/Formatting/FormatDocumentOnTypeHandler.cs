﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Indentation;
using Microsoft.CodeAnalysis.Options;
using Roslyn.LanguageServer.Protocol;

namespace Microsoft.CodeAnalysis.LanguageServer.Handler;

[ExportCSharpVisualBasicStatelessLspService(typeof(FormatDocumentOnTypeHandler)), Shared]
[Method(Methods.TextDocumentOnTypeFormattingName)]
internal sealed class FormatDocumentOnTypeHandler : ILspServiceDocumentRequestHandler<DocumentOnTypeFormattingParams, TextEdit[]?>
{
    private readonly IGlobalOptionService _globalOptions;

    public bool MutatesSolutionState => false;
    public bool RequiresLSPSolution => true;

    [ImportingConstructor]
    [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
    public FormatDocumentOnTypeHandler(IGlobalOptionService globalOptions)
    {
        _globalOptions = globalOptions;
    }

    public TextDocumentIdentifier GetTextDocumentIdentifier(DocumentOnTypeFormattingParams request) => request.TextDocument;

    public async Task<TextEdit[]?> HandleRequestAsync(
        DocumentOnTypeFormattingParams request,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var document = context.Document;
        if (document is null)
            return null;

        if (string.IsNullOrEmpty(request.Character))
        {
            return [];
        }

        var position = await document.GetPositionFromLinePositionAsync(ProtocolConversions.PositionToLinePosition(request.Position), cancellationToken).ConfigureAwait(false);

        var formattingService = document.Project.Services.GetRequiredService<ISyntaxFormattingService>();
        var documentSyntax = await ParsedDocument.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        if (!formattingService.ShouldFormatOnTypedCharacter(documentSyntax, request.Character[0], position, cancellationToken))
        {
            return [];
        }

        // We should use the options passed in by LSP instead of the document's options.
        var formattingOptions = await ProtocolConversions.GetFormattingOptionsAsync(request.Options, document, cancellationToken).ConfigureAwait(false);
        var indentationOptions = new IndentationOptions(formattingOptions)
        {
            AutoFormattingOptions = _globalOptions.GetAutoFormattingOptions(document.Project.Language)
        };

        var textChanges = formattingService.GetFormattingChangesOnTypedCharacter(documentSyntax, position, indentationOptions, cancellationToken);
        if (textChanges.IsEmpty)
        {
            return [];
        }

        if (SyntaxFacts.IsNewLine(request.Character[0]))
        {
            // When formatting after a newline is pressed, the cursor line will be all whitespace
            // and we do not want to remove the indentation from it.
            //
            // Take the following example of pressing enter after an opening brace.
            //
            // ```
            //    public void M() {||}
            // ```
            //
            // The editor moves the cursor to the next line and uses it's languageconfig to add
            // the appropriate level of indentation.
            //
            // ```
            //     public void M() {
            //         ||
            //     }
            // ```
            //
            // At this point `formatOnType` is called. The formatting service will generate two
            // text changes. The first moves the opening brace to a new line with proper
            // indentation. The second removes the whitespace from the cursor line and rewrites
            // the indentation prior to the closing brace.
            // 
            // Letting the second change go through would be a bad experience for the user as they
            // will now be responsible for adding back the proper indentation.

            textChanges = textChanges.WhereAsArray(static (change, position) => !change.Span.Contains(position), position);
        }

        return [.. textChanges.Select(change => ProtocolConversions.TextChangeToTextEdit(change, documentSyntax.Text))];
    }
}
