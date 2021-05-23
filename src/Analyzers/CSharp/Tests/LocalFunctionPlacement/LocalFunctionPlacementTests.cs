using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Editor.UnitTests.CodeActions;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.Analyzers.UnitTests.LocalFunctionPlacement
{
    using VerifyCS = CSharpCodeFixVerifier<
        Microsoft.CodeAnalysis.CSharp.Analyzers.LocalFunctionPlacement.RequireLocalFunctionAtEndOfBlockAnalyzer,
        Microsoft.CodeAnalysis.Testing.EmptyCodeFixProvider>;
    public class LocalFunctionPlacementTests
    {
        [Fact]
        public async Task TestSimpleCase()
        {
            var source = @"
class C
{
    int M()
    {
        void L() { }

        int x = 0;
        return x;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(source);
        }
    }
}
