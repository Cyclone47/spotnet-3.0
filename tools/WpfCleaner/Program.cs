using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace WpfCleaner
{
    class Program
    {
        static void Main(string[] args)
        {
            string spotnetDir = @"d:\sourcecode\src\Spotnet\Spotnet";
            string origDir = @"d:\sourcecode\decompiled_200\Spotnet";

            // 1. Restore all original decompiled C# files from decompiled_200
            Console.WriteLine("Restoring original decompiled C# files...");
            foreach (var origFile in Directory.GetFiles(origDir, "*.cs", SearchOption.AllDirectories))
            {
                if (origFile.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)) continue;
                string rel = Path.GetRelativePath(origDir, origFile);
                string target = Path.Combine(spotnetDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(origFile, target, true);
            }

            // 2. Map every XAML file to its declared x:Class and its element names
            var classToXamlElements = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var xamlFiles = Directory.GetFiles(spotnetDir, "*.xaml", SearchOption.AllDirectories);

            foreach (var xamlFile in xamlFiles)
            {
                string text = File.ReadAllText(xamlFile);
                var classMatch = Regex.Match(text, @"x:Class\s*=\s*""([^""]+)""");
                if (classMatch.Success)
                {
                    string className = classMatch.Groups[1].Value.Split('.').Last();
                    if (!classToXamlElements.TryGetValue(className, out var set))
                    {
                        set = new HashSet<string>(StringComparer.Ordinal);
                        classToXamlElements[className] = set;
                    }

                    var nameMatches = Regex.Matches(text, @"(?:x:Name|Name)\s*=\s*""([^""]+)""");
                    foreach (Match m in nameMatches)
                    {
                        set.Add(m.Groups[1].Value);
                    }
                }
            }

            Console.WriteLine($"Mapped {classToXamlElements.Count} XAML classes with UI elements.");

            var csFiles = Directory.GetFiles(spotnetDir, "*.cs", SearchOption.AllDirectories);
            int cleanedCount = 0;

            foreach (var file in csFiles)
            {
                if (file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                    continue;
                }

                string code = File.ReadAllText(file);
                if (code.Contains("IComponentConnector") || code.Contains("InitializeComponent()"))
                {
                    string newCode = CleanCodeBehindFile(code, classToXamlElements);
                    if (newCode != code)
                    {
                        File.WriteAllText(file, newCode);
                        Console.WriteLine($"[CLEANED] {Path.GetFileName(file)}");
                        cleanedCount++;
                    }
                }
            }

            Console.WriteLine($"Finished cleaning {cleanedCount} code-behind files.");

            // Post-clean fixes:
            // 1. App.cs: Remove manual Main()
            string appFile = Path.Combine(spotnetDir, @"Spotnet\App.cs");
            if (File.Exists(appFile))
            {
                string appCode = File.ReadAllText(appFile);
                appCode = Regex.Replace(appCode, @"\[STAThread\]\s*\[DebuggerNonUserCode\][\s\S]*?public static void Main\(\)\s*\{[\s\S]*?\}", "");
                File.WriteAllText(appFile, appCode);
                Console.WriteLine("[FIXED] Removed Main() from App.cs");
            }

            // 2. mshtml\HTMLAnchorEvents_Event.cs: Add Guid
            string htmlEventFile = Path.Combine(spotnetDir, @"mshtml\HTMLAnchorEvents_Event.cs");
            if (File.Exists(htmlEventFile))
            {
                string htmlEventCode = File.ReadAllText(htmlEventFile);
                if (!htmlEventCode.Contains("[Guid("))
                {
                    htmlEventCode = htmlEventCode.Replace("[ComImport]", "[ComImport]\n[Guid(\"3050f1c5-98b5-11cf-bb82-00aa00bdce0b\")]");
                    File.WriteAllText(htmlEventFile, htmlEventCode);
                    Console.WriteLine("[FIXED] Added Guid to HTMLAnchorEvents_Event.cs");
                }
            }
        }

        static string CleanCodeBehindFile(string code, Dictionary<string, HashSet<string>> classToXamlElements)
        {
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = (CompilationUnitSyntax)tree.GetRoot();

            var rewriter = new PreciseWpfRewriter(classToXamlElements);
            var newRoot = rewriter.Visit(root);

            return newRoot.NormalizeWhitespace().ToFullString();
        }
    }

    class PreciseWpfRewriter : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, HashSet<string>> _classToXamlElements;

        public PreciseWpfRewriter(Dictionary<string, HashSet<string>> classToXamlElements)
        {
            _classToXamlElements = classToXamlElements;
        }

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            string className = node.Identifier.Text;
            bool isWpfClass = node.BaseList != null && node.BaseList.Types.Any(t =>
            {
                string b = t.Type.ToString();
                return b.Contains("Window") || b.Contains("UserControl") || b.Contains("Page") || b.Contains("Application") || b.Contains("IComponentConnector");
            });

            if (!isWpfClass && !_classToXamlElements.ContainsKey(className))
            {
                return base.VisitClassDeclaration(node);
            }

            // Make partial, remove sealed
            var modifiers = node.Modifiers.Where(m => !m.IsKind(SyntaxKind.SealedKeyword) && !m.IsKind(SyntaxKind.PartialKeyword)).ToList();
            modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword));

            // Filter base list to remove IComponentConnector and IStyleConnector
            BaseListSyntax? newBaseList = node.BaseList;
            if (newBaseList != null)
            {
                var filteredTypes = newBaseList.Types.Where(t =>
                {
                    string name = t.Type.ToString();
                    return !name.Contains("IComponentConnector") && !name.Contains("IStyleConnector");
                }).ToArray();

                if (filteredTypes.Length == 0)
                {
                    newBaseList = null;
                }
                else
                {
                    newBaseList = SyntaxFactory.BaseList(SyntaxFactory.SeparatedList(filteredTypes));
                }
            }

            _classToXamlElements.TryGetValue(className, out var elementNames);

            var newMembers = node.Members.Where(m =>
            {
                if (m is FieldDeclarationSyntax field)
                {
                    var varNames = field.Declaration.Variables.Select(v => v.Identifier.Text).ToList();
                    if (varNames.Contains("_contentLoaded")) return false;

                    // Remove field ONLY if it is declared in the XAML of THIS class and is internal/public
                    bool isInternalOrPublic = field.Modifiers.Any(mod => mod.IsKind(SyntaxKind.InternalKeyword) || mod.IsKind(SyntaxKind.PublicKeyword));
                    if (isInternalOrPublic && elementNames != null && varNames.Any(v => elementNames.Contains(v)))
                    {
                        return false;
                    }
                }

                if (m is MethodDeclarationSyntax method)
                {
                    string methodName = method.Identifier.Text;
                    if (methodName == "InitializeComponent") return false;
                    if (methodName == "_CreateDelegate") return false;
                    if (method.ExplicitInterfaceSpecifier != null)
                    {
                        string iface = method.ExplicitInterfaceSpecifier.Name.ToString();
                        if (iface.Contains("IComponentConnector") || iface.Contains("IStyleConnector"))
                            return false;
                    }
                }

                return true;
            }).ToList();

            var updatedClass = node.WithModifiers(SyntaxFactory.TokenList(modifiers))
                                   .WithBaseList(newBaseList)
                                   .WithMembers(SyntaxFactory.List(newMembers));

            return base.VisitClassDeclaration(updatedClass);
        }
    }
}
