using Meridian.Application.Common;

namespace Meridian.Application.Ports;

public interface ITemplateRenderer
{
    ServiceResult<string> Render(string template, IDictionary<string, object> tokens);
}
