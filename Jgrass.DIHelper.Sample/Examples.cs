using System.Collections.Generic;
using System.Runtime.InteropServices.ComTypes;

namespace Jgrass.DIHelper.Sample;

public interface IMyService { }

public interface IMyService2 { }

public partial class ExampleViewModel
{
    [Autowired]
    public partial IMyService MyService { get; }

    [Autowired]
    public partial IMyService2 MyService2 { get; }
}

public class App
{
    [AutowiredGetter]
    public static T GetDIService<T>()
        where T : class
    {
        return default;
    }
}
