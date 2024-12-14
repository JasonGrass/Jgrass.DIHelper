using Jgrass.DIHelper.Sample.Services;

namespace Jgrass.DIHelper.Sample.ViewModels;

public partial class ExampleViewModel
{
    [Autowired]
    public partial IMyService MyService { get; }

    [Autowired]
    public partial IMyService2 MyService2 { get; }
}
