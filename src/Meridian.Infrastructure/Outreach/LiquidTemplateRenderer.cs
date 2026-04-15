using DotLiquid;
using Meridian.Application.Common;
using Meridian.Application.Ports;

namespace Meridian.Infrastructure.Outreach;

public class LiquidTemplateRenderer : ITemplateRenderer
{
    public ServiceResult<string> Render(string template, IDictionary<string, object> tokens)
    {
        try
        {
            var parsed = Template.Parse(template);
            if (parsed.Errors.Count > 0)
            {
                var errors = string.Join("; ", parsed.Errors.Select(e => e.Message));
                return ServiceResult<string>.Fail($"Template parse errors: {errors}");
            }

            var renderParams = new RenderParameters(System.Globalization.CultureInfo.InvariantCulture)
            {
                LocalVariables = BuildHash(tokens),
                ErrorsOutputMode = ErrorsOutputMode.Suppress
            };
            var rendered = parsed.Render(renderParams);

            return ServiceResult<string>.Ok(rendered);
        }
        catch (Exception ex)
        {
            return ServiceResult<string>.Fail($"Template rendering failed: {ex.Message}");
        }
    }

    private static Hash BuildHash(IDictionary<string, object> tokens)
    {
        var hash = new Hash();
        foreach (var (key, value) in tokens)
        {
            if (value is IDictionary<string, object> nested)
                hash[key] = BuildHash(nested);
            else
                hash[key] = value;
        }
        return hash;
    }
}
