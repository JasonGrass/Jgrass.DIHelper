using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Jgrass.DIHelper;

[Generator]
public class AutowiredSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 获取所有的语法树
        var syntaxProvider = context
            .SyntaxProvider.CreateSyntaxProvider(predicate: (node, _) => IsSyntaxNodeOfInterest(node), transform: (ctx, _) => TransformNode(ctx))
            .Where(x => x is not null);

        // 收集标记了 [Autowired] 的属性信息
        var autowiredProperties = syntaxProvider
            .Where(x => x?.Kind == NodeKind.Property && x.Symbol is IPropertySymbol)
            .Select((x, _) => (IPropertySymbol)x!.Symbol!);

        // 收集标记了 [AutowiredGetter] 的方法信息
        var autowiredGetterMethods = syntaxProvider
            .Where(x => x?.Kind == NodeKind.Method && x.Symbol is IMethodSymbol)
            .Select((x, _) => (IMethodSymbol)x!.Symbol!);

        // 将所有信息汇总
        var combined = autowiredProperties.Collect().Combine(autowiredGetterMethods.Collect());

        context.RegisterSourceOutput(
            combined,
            (spc, source) =>
            {
                var (properties, methods) = source;

                if (!properties.Any())
                {
                    return;
                }

                // 如果有属性标记了 [Autowired]，则必须有对应的 AutowiredGetter 方法
                if (properties.Any() && !methods.Any())
                {
                    // 报错：没有找到 AutowiredGetter 方法
                    foreach (var prop in properties)
                    {
                        var diag = Diagnostic.Create(
                            new DiagnosticDescriptor(
                                id: "AW001",
                                title: "No AutowiredGetter found",
                                messageFormat: "Found [Autowired] properties but no [AutowiredGetter] method was found.",
                                category: "AutowiredInjection",
                                DiagnosticSeverity.Error,
                                isEnabledByDefault: true
                            ),
                            prop.Locations.FirstOrDefault()
                        );
                        spc.ReportDiagnostic(diag);
                    }
                    return;
                }

                if (methods.Length > 1)
                {
                    var diag = Diagnostic.Create(
                        new DiagnosticDescriptor(
                            id: "AW002",
                            title: "Too many AutowiredGetter found",
                            messageFormat: "The [AutowiredGetter] method can only have one.",
                            category: "AutowiredInjection",
                            DiagnosticSeverity.Error,
                            isEnabledByDefault: true
                        ),
                        methods.First().Locations.FirstOrDefault()
                    );
                    spc.ReportDiagnostic(diag);
                }

                // 校验 AutowiredGetter 方法（这里假设最多存在一个合规方法，若有多个可自行规定策略）
                IMethodSymbol getterMethod = methods.First();

                if (!IsValidAutowiredGetterMethod(getterMethod))
                {
                    var diag = Diagnostic.Create(
                        new DiagnosticDescriptor(
                            id: "AW003",
                            title: "Invalid AutowiredGetter",
                            messageFormat: "[AutowiredGetter] method is invalid.",
                            category: "AutowiredInjection",
                            DiagnosticSeverity.Error,
                            isEnabledByDefault: true
                        ),
                        getterMethod.Locations.FirstOrDefault()
                    );
                    spc.ReportDiagnostic(diag);
                    return;
                }

                // 对所有属性进行代码生成
                // 按照类分组
                var groups = properties.GroupBy(p => p.ContainingType, SymbolEqualityComparer.Default);
                foreach (var group in groups)
                {
                    var classSymbol = group.Key as INamedTypeSymbol;
                    if (classSymbol is null)
                    {
                        continue;
                    }

                    if (!IsPartialClass(classSymbol))
                    {
                        // 报错：类必须为 partial
                        var diag = Diagnostic.Create(
                            new DiagnosticDescriptor(
                                id: "AW004",
                                title: "Class must be partial",
                                messageFormat: "Class {0} with [Autowired] properties must be declared as partial.",
                                category: "AutowiredInjection",
                                DiagnosticSeverity.Error,
                                isEnabledByDefault: true
                            ),
                            classSymbol.Locations.FirstOrDefault(),
                            classSymbol.Name
                        );
                        spc.ReportDiagnostic(diag);
                        continue;
                    }

                    // 生成partial类代码
                    var sourceCode = GeneratePartialClassCode(classSymbol, group.ToList(), getterMethod);
                    spc.AddSource($"{classSymbol.Name}_Autowired.g.cs", sourceCode);
                }
            }
        );
    }

    private static bool IsSyntaxNodeOfInterest(SyntaxNode node)
    {
        // 我们关心的结点类型是带有 Autowired 或 AutowiredGetter 特性的属性或方法
        return node is PropertyDeclarationSyntax propertyDecl && propertyDecl.AttributeLists.Count > 0
            || node is MethodDeclarationSyntax methodDecl && methodDecl.AttributeLists.Count > 0;
    }

    private static NodeInfo? TransformNode(GeneratorSyntaxContext context)
    {
        var node = context.Node;
        var semanticModel = context.SemanticModel;

        if (node is PropertyDeclarationSyntax propertyDecl)
        {
            if (semanticModel.GetDeclaredSymbol(propertyDecl) is { } symbol && HasAttribute(symbol, "AutowiredAttribute"))
            {
                // 人类补充：C#13 引入了 partial property 的语法，但 GPT-o1 似乎还不知道，以下是 o1 写的注释。
                // 总得来说，是可以工作的。

                // 校验属性是否 partial
                // C# 目前没有 partial 的 Property 概念，但可以通过 partial class + partial method 的 trick 实现
                // 此处理解为用户的需求：该属性声明所在类为 partial 类，属性本身为 auto-property 且声明为 partial?
                // 问题描述使用 partial property 也许只是为了强调使用 partial 类的方式注入。这里我们严格检查注释中要求。
                // 要求：标记了 Autowired 的属性，必须是 partial 的。
                // C# 中无法直接声明 partial property，但从描述看例子是 public partial IMyService MyService { get; }
                // 实际上 partial 属性语法不存在，这里理解为用户的意思是属性所在类需要 partial 并且属性声明中有 partial 关键字 ( C#9+ 不支持partial properties)
                // 为了满足需求，这里检查下 propertyDecl 中的 Modifiers 是否包含 partial
                // 如果没有 partial 关键字，会报错。
                if (!propertyDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                {
                    return new NodeInfo(NodeKind.Property, symbol)
                    {
                        HasError = true,
                        ErrorId = "AW005",
                        ErrorMessage = $"Property {symbol.Name} must be declared as partial.",
                    };
                }

                // 检查访问器，只能有 get，没有 set/init
                if (!HasOnlyGetAccessor(propertyDecl))
                {
                    return new NodeInfo(NodeKind.Property, symbol)
                    {
                        HasError = true,
                        ErrorId = "AW006",
                        ErrorMessage = $"Property {symbol.Name} with [Autowired] must have only a get accessor.",
                    };
                }

                return new NodeInfo(NodeKind.Property, symbol);
            }
        }

        if (node is MethodDeclarationSyntax methodDecl)
        {
            if (semanticModel.GetDeclaredSymbol(methodDecl) is { } symbol && HasAttribute(symbol, "AutowiredGetterAttribute"))
            {
                // 是标记了AutowiredGetter的方法
                return new NodeInfo(NodeKind.Method, symbol);
            }
        }

        return null;
    }

    private static bool HasOnlyGetAccessor(PropertyDeclarationSyntax prop)
    {
        if (prop.AccessorList == null)
            return false;
        var accessors = prop.AccessorList.Accessors;
        if (accessors.Count != 1)
            return false;
        var accessor = accessors[0];
        return accessor.IsKind(SyntaxKind.GetAccessorDeclaration) && accessor.Body == null && accessor.ExpressionBody == null;
    }

    private static bool HasAttribute(ISymbol symbol, string attributeName)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name == attributeName)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 判断类是否包含 partial 关键字
    /// </summary>
    /// <param name="classSymbol"></param>
    /// <returns></returns>
    private static bool IsPartialClass(INamedTypeSymbol classSymbol)
    {
        foreach (var decl in classSymbol.DeclaringSyntaxReferences)
        {
            if (decl.GetSyntax() is TypeDeclarationSyntax syntax && syntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsValidAutowiredGetterMethod(IMethodSymbol methodSymbol)
    {
        // 要求：
        // 标记AutowiredGetter的方法必须是静态方法
        // 方法签名：public static T GetService<T>() where T:class
        // 检查泛型、返回值、约束等
        if (!methodSymbol.IsStatic)
            return false;
        if (!methodSymbol.IsGenericMethod)
            return false;
        if (methodSymbol.TypeParameters.Length != 1)
            return false;
        var tParam = methodSymbol.TypeParameters[0];
        if (!tParam.HasReferenceTypeConstraint)
            return false;
        if (!SymbolEqualityComparer.Default.Equals(methodSymbol.ReturnType, tParam))
            return false;

        // 检查命名规则，这里不强制GetService前缀，只要满足泛型签名即可，也可根据需求定制。
        return true;
    }

    private static string GeneratePartialClassCode(INamedTypeSymbol classSymbol, List<IPropertySymbol> properties, IMethodSymbol getterMethod)
    {
        // 获取目标类所在的命名空间
        var ns = classSymbol.ContainingNamespace.IsGlobalNamespace ? "" : $"namespace {classSymbol.ContainingNamespace.ToDisplayString()};";

        // 准备using
        var usings = new HashSet<string>();
        // 将 getterMethod 所在类的命名空间加入using
        if (!getterMethod.ContainingNamespace.IsGlobalNamespace)
        {
            usings.Add($"using {getterMethod.ContainingNamespace.ToDisplayString()};");
        }

        // 对于属性类型引用的namespace也需要
        foreach (var prop in properties)
        {
            var propNs = prop.Type.ContainingNamespace;
            if (propNs != null && !propNs.IsGlobalNamespace)
            {
                usings.Add($"using {propNs.ToDisplayString()};");
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine(ns);
        sb.AppendLine();
        foreach (var u in usings)
        {
            sb.AppendLine(u);
        }
        sb.AppendLine();
        sb.AppendLine($"partial class {classSymbol.Name}");
        sb.AppendLine("{");

        // 为每个 property 生成实现
        // public partial IMyService MyService => App.GetService<IMyService>();
        foreach (var prop in properties)
        {
            var propName = prop.Name;
            var propType = prop.Type.ToDisplayString();
            var getterClassName = getterMethod.ContainingType.ToDisplayString();

            sb.AppendLine($"    public partial {propType} {propName} => {getterClassName}.{getterMethod.Name}<{propType}>();");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private enum NodeKind
    {
        None,
        Property,
        Method,
    }

    private class NodeInfo(NodeKind kind, ISymbol? symbol)
    {
        public NodeKind Kind { get; } = kind;
        public ISymbol? Symbol { get; } = symbol;
        public bool HasError { get; set; } = false;
        public string ErrorId { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
