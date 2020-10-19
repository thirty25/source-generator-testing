using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace SourceGen.Tests
{
    public class UnitTest1
    {
        [Fact]
        public void HelloWorld_is_injected()
        {
            const string userSource = @"
namespace Example
{
    [AutoHelloWorldGenerator.AutoHelloWorld]
    public partial class TestClass
    {
        public string Goodbye() {
            return ""Goodbye, world"";
        }
    }
}
";
            // compile our source and run a generator against it
            var comp = CreateCompilation(userSource);
            var (newComp, generatorDiagnostics) = RunGenerators(comp, new HelloWorldGenerator());

            // verify no errors or warnings are returned
            Assert.Empty(generatorDiagnostics);
            Assert.Empty(newComp.GetDiagnostics());

            // compile and get an assembly along with our methods.
            var assembly = GetAssemblyFromCompilation(newComp);
            var type = assembly.GetType("Example.TestClass");
            var goodbye = type?.GetMethod("Goodbye"); // this is in our source
            var hello = type?.GetMethod("Hello"); // this one is added via the generator

            Assert.NotNull(goodbye);
            Assert.NotNull(hello);

            var example = Activator.CreateInstance(type);
            var goodbyeReturn = (string)goodbye.Invoke(example, null);
            var helloReturn = (string)hello.Invoke(example, null);

            Assert.Equal("Hello, World", helloReturn);
            Assert.Equal("Goodbye, world", goodbyeReturn);
        }

        [Fact]
        public void Non_partial_classes_error()
        {
            const string userSource = @"
namespace Example
{
    [AutoHelloWorldGenerator.AutoHelloWorld]
    public class TestClass
    {
        public string Goodbye() {
            return ""Goodbye, world"";
        }
    }
}
";
            // compile our source and run a generator against it
            var comp = CreateCompilation(userSource);
            var (_, generatorDiagnostics) = RunGenerators(comp, new HelloWorldGenerator());
            Assert.Contains(generatorDiagnostics, diagnostic => diagnostic.Id == "HW001");
        }

        private static Assembly GetAssemblyFromCompilation(Compilation newComp)
        {
            using var stream = new MemoryStream();
            newComp.Emit(stream);
            var assembly = Assembly.Load(stream.ToArray());
            return assembly;
        }

        private static Compilation CreateCompilation(string source)
        {
            var dd = typeof(Enumerable).GetTypeInfo().Assembly.Location;
            var coreDir = Directory.GetParent(dd) ?? throw new Exception("Couldn't find location of coredir");

            var references = new[]
            {
                MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Dictionary<,>).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(coreDir.FullName + Path.DirectorySeparatorChar + "mscorlib.dll"),
                MetadataReference.CreateFromFile(coreDir.FullName + Path.DirectorySeparatorChar +
                                                 "System.Runtime.dll"),
                MetadataReference.CreateFromFile(coreDir.FullName + Path.DirectorySeparatorChar +
                                                 "System.Collections.dll"),
            };


            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            return CSharpCompilation.Create(
                "compilation",
                new[] {syntaxTree},
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static GeneratorDriver CreateDriver(Compilation c, params ISourceGenerator[] generators)
        {
            var parseOptions = (CSharpParseOptions)c.SyntaxTrees.First().Options;

            return CSharpGeneratorDriver.Create(
                ImmutableArray.Create(generators),
                ImmutableArray<AdditionalText>.Empty,
                parseOptions);
        }

        private static (Compilation, ImmutableArray<Diagnostic> diagnostics) RunGenerators(
            Compilation c,
            params ISourceGenerator[] generators)
        {
            CreateDriver(c, generators).RunGeneratorsAndUpdateCompilation(c, out var d, out var diagnostics);
            return (d, diagnostics);
        }
    }
}
