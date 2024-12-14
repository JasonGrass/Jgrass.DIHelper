# Jgrass.DIHelper

Dependency Injection Helper

通过代码生成器，完成对依赖的自动注入，无需任何运行时的反射操作。

仅支持 `属性` 的自动注入。

## 实现原理

使用 C#13 的 [partial property](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-13.0/partial-properties ) 特性。

如果发现了标记 `[Autowired]` 的分部属性，则自动生成相关的对应的属性 getter。

```cs
// 用户代码
public partial class ExampleViewModel
{
    [Autowired]
    public partial IMyService MyService { get; }

    [Autowired]
    public partial IMyService2 MyService2 { get; }
}

// 自动生成的代码
partial class ExampleViewModel
{
    public partial IMyService MyService => App.GetService<Jgrass.DIHelper.Sample.Services.IMyService>();
    public partial IMyService2 MyService2 => App.GetService<Jgrass.DIHelper.Sample.Services.IMyService2>();
}
```

使用 `[AutowiredGetter]` 标记从容器中获取服务的方法。

```cs
public class App
{
    [AutowiredGetter]
    public static T GetService<T>()
        where T : class
    {
        return default;
    }
}
```

## 使用要求

### [Autowired] 属性

- 必须标记 `partial`，也就是要求 C# 版本至少是 13
- 必须有且仅有 `get` 访问器

### [AutowiredGetter] 方法

- 必须是静态方法
- 只能有一个方法标记为 AutowiredGetter
- 方法签名必须是 `T MethodName<T>()` (方法名不限)

建议：需要在这个方法中，保证服务的获取，如果获取到的是空，应该抛异常。
