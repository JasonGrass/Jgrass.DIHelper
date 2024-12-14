using System.Collections.Generic;
using System.Runtime.InteropServices.ComTypes;

namespace Jgrass.DIHelper.Sample;

public class App
{
    [AutowiredGetter]
    public static T GetDIService<T>()
        where T : class
    {
        return default;
    }
}
