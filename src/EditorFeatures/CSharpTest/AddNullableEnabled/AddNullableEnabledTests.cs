using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.CodeFixes.AddNullableEnabled;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.Diagnostics;
using Microsoft.CodeAnalysis.Editor.UnitTests.CodeActions;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.CodeAnalysis.CSharp.Analyzers.UnitTests.AddNullableEnabled
{
    public class AddNullableEnabledTests : AbstractCSharpDiagnosticProviderBasedUserDiagnosticTest
    {
        private static readonly CompilationOptions WarningEnableCompilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Warnings);
        public AddNullableEnabledTests(ITestOutputHelper logger) : base(logger)
        {
        }

        [Fact]
        public Task TestRemoveDisabled()
        {
            var source = @"
#nullable disable
class C
{
    public C[||]? Other;
}";

            var fixedSource = @"
class C
{
    public C? Other;
}";

            return TestInRegularAndScriptAsync(source, fixedSource, compilationOptions: WarningEnableCompilationOptions);
        }

        [Fact]
        public Task TestRemoveDisabled_RandomLocation()
        {
            var source = @"
class A
{
#nullable disable
}

class C
{
    public C[||]? Other;
}";

            var fixedSource = @"
class A
{
}

class C
{
    public C? Other;
}";

            return TestInRegularAndScriptAsync(source, fixedSource, compilationOptions: WarningEnableCompilationOptions);
        }

        [Fact]
        public Task TestRemoveDisabled_MultipleLocations()
        {
            var source = @"
class A
{
#nullable disable
#nullable enable
}

#nullable disable
class C
{
    public C[||]? Other;
}

#nullable enable";

            var fixedSource = @"
class A
{
#nullable disable
#nullable enable
}

class C
{
    public C[||]? Other;
}

#nullable enable";

            return TestInRegularAndScriptAsync(source, fixedSource, compilationOptions: WarningEnableCompilationOptions);
        }

        [Fact]
        public Task TestRemoveDisabled_MultipleDisables()
        {
            var source = @"
class A
{
}

#nullable disable
#nullable disable
class C
{
    public C[||]? Other;
}";

            var fixedSource = @"
class A
{
}

class C
{
    public C[||]? Other;
}";

            return TestInRegularAndScriptAsync(source, fixedSource, compilationOptions: WarningEnableCompilationOptions);
        }

        internal override (DiagnosticAnalyzer?, CodeFixProvider) CreateDiagnosticProviderAndFixer(Workspace workspace)
            => (null, new AddNullableEnabledContextCodeFixProvider());
    }
}
