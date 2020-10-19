using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SourceGen
{
    [Generator]
    public class HelloWorldGenerator : ISourceGenerator
    {
        private const string AttributeText = @"
using System;
namespace AutoHelloWorldGenerator
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    sealed class AutoHelloWorldAttribute : Attribute
    {
        public AutoHelloWorldAttribute()
        {
        }
        public string Name { get; set; }
    }
}
";

        public void Execute(GeneratorExecutionContext context)
        {
            // always inject the attribute
            context.AddSource("AutoHelloWorldAttribute", SourceText.From(AttributeText, Encoding.UTF8));

            // we set up the syntax receiver in the initialize method. it looks for candidate classes to generate
            // code for. if we don't have this for some reason we should bounce. or maybe even fail
            if (!(context.SyntaxReceiver is SyntaxReceiver receiver))
                return;

            var sourceBuilder = new StringBuilder();
            foreach (var classSyntax in receiver.CandidatesClasses)
            {
                // our code only works for partial classes. if we don't have one we want to return an error
                if (classSyntax.Modifiers.Any(i => i.Kind() == SyntaxKind.PartialKeyword) == false)
                {
                    var rule = new DiagnosticDescriptor(
                        "HW001",
                        "Hello World Generator",
                        "Only partial classes are supported",
                        "HelloWorld.Generator",
                        DiagnosticSeverity.Error,
                        true);

                    context.ReportDiagnostic(Diagnostic.Create(rule, classSyntax.GetLocation()));
                }


                // get the namespace of the inspected class so that our new one matches
                var @namespace = string.Join(".", GetNamespaces(classSyntax));
                var source = @$"
namespace {@namespace}
{{
        public partial class {classSyntax.Identifier.ToString()} 
        {{            
            public string Hello() 
            {{
                return ""Hello, World"";
            }}
        }}
}}        
";
                sourceBuilder.AppendLine(source);
            }

            context.AddSource("helloWorldGenerated", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        private static IEnumerable<string> GetNamespaces(SyntaxNode styledClass)
        {
            // probably a smarter way to do this
            var currentNode = styledClass.Parent;
            while (currentNode != null)
            {
                if (currentNode.Kind() == SyntaxKind.NamespaceDeclaration)
                {
                    yield return ((NamespaceDeclarationSyntax)currentNode).Name.ToString();
                }

                currentNode = currentNode.Parent;
            }
        }

        internal class SyntaxReceiver : ISyntaxReceiver
        {
            public List<ClassDeclarationSyntax> CandidatesClasses { get; } = new List<ClassDeclarationSyntax>();

            /// <summary>
            /// Called for every syntax node in the compilation, we can inspect the nodes and save any information useful for generation
            /// </summary>
            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                // any field with at least one attribute is a candidate for property generation
                if (syntaxNode is not ClassDeclarationSyntax classDeclarationSyntax)
                {
                    return;
                }

                if (classDeclarationSyntax.AttributeLists.Any(list => list.Attributes.Any(i => i.Name.ToString() == "AutoHelloWorldGenerator.AutoHelloWorld")))
                {
                    CandidatesClasses.Add(classDeclarationSyntax);
                }
            }
        }
    }
}
