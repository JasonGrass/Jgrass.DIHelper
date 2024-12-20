using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Jgrass.DIHelper;

[Generator]
public class AutowiredAttributeSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 在源码生成器初始化时，直接输出所需的 Attributes 定义文件
        context.RegisterPostInitializationOutput(ctx =>
        {
            var source =
                @"// <auto-generated />
using System;

[AttributeUsage(AttributeTargets.Property)]
public class AutowiredAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method)]
public class AutowiredGetterAttribute : Attribute
{
}
";
            ctx.AddSource("AutowiredAttributes.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }
}
