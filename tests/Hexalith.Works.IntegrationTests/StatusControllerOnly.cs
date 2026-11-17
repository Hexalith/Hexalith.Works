using Hexalith.EventStore.Controllers;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Hexalith.Works.IntegrationTests;

internal sealed class StatusControllerOnly : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        foreach (TypeInfo controller in feature.Controllers.Where(controller => controller.AsType() != typeof(CommandStatusController)).ToArray())
            feature.Controllers.Remove(controller);
    }
}
