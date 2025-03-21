﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.FindUsages;
using Microsoft.CodeAnalysis.Shared.Extensions;

namespace Microsoft.CodeAnalysis.GoToBase;

internal abstract class AbstractGoToBaseService : IGoToBaseService
{
    protected abstract Task<IMethodSymbol?> FindNextConstructorInChainAsync(
        Solution solution, IMethodSymbol constructor, CancellationToken cancellationToken);

    protected static IMethodSymbol? FindBaseNoArgConstructor(IMethodSymbol constructor)
    {
        var baseType = constructor.ContainingType.BaseType;
        if (baseType is null)
            return null;

        return baseType.InstanceConstructors.FirstOrDefault(
            baseConstructor => baseConstructor.IsAccessibleWithin(constructor.ContainingType) &&
                baseConstructor.Parameters.All(p => p.IsOptional || p.IsParams));
    }

    public async Task FindBasesAsync(IFindUsagesContext context, Document document, int position, OptionsProvider<ClassificationOptions> classificationOptions, CancellationToken cancellationToken)
    {
        var symbolAndProject = await FindUsagesHelpers.GetRelevantSymbolAndProjectAtPositionAsync(
            document, position, cancellationToken).ConfigureAwait(false);

        if (symbolAndProject is not var (symbol, project))
        {
            await context.ReportNoResultsAsync(
                FeaturesResources.Cannot_navigate_to_the_symbol_under_the_caret, cancellationToken).ConfigureAwait(false);
            return;
        }

        var solution = project.Solution;
        var bases = FindBaseHelpers.FindBases(symbol, solution, cancellationToken);
        if (bases.Length == 0 && symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
        {
            var nextConstructor = await FindNextConstructorInChainAsync(solution, constructor, cancellationToken).ConfigureAwait(false);
            if (nextConstructor != null)
                bases = [nextConstructor];
        }

        await context.SetSearchTitleAsync(
            string.Format(FeaturesResources._0_bases,
            FindUsagesHelpers.GetDisplayName(symbol)),
            cancellationToken).ConfigureAwait(false);

        var found = false;

        // For each potential base, try to find its definition in sources.
        // If found, add it's definitionItem to the context.
        // If not found but the symbol is from metadata, create it's definition item from metadata and add to the context.
        foreach (var baseSymbol in bases)
        {
            var sourceDefinition = SymbolFinder.FindSourceDefinition(
               baseSymbol, solution, cancellationToken);
            if (sourceDefinition != null)
            {
                var definitionItem = await sourceDefinition.ToClassifiedDefinitionItemAsync(
                    classificationOptions, solution, FindReferencesSearchOptions.Default, isPrimary: true, includeHiddenLocations: false, cancellationToken: cancellationToken).ConfigureAwait(false);

                await context.OnDefinitionFoundAsync(definitionItem, cancellationToken).ConfigureAwait(false);
                found = true;
            }
            else if (baseSymbol.Locations.Any(static l => l.IsInMetadata))
            {
                var definitionItem = baseSymbol.ToNonClassifiedDefinitionItem(
                    solution, FindReferencesSearchOptions.Default, includeHiddenLocations: true);
                await context.OnDefinitionFoundAsync(definitionItem, cancellationToken).ConfigureAwait(false);
                found = true;
            }
        }

        if (!found)
        {
            await context.ReportNoResultsAsync(FeaturesResources.The_symbol_has_no_base, cancellationToken).ConfigureAwait(false);
        }
    }
}
