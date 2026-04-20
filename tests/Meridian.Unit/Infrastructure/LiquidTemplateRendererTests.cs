using FluentAssertions;
using Meridian.Infrastructure.Outreach;

namespace Meridian.Unit.Infrastructure;

public class LiquidTemplateRendererTests
{
    private readonly LiquidTemplateRenderer _renderer = new();

    [Fact]
    public void Renders_simple_token()
    {
        var tokens = new Dictionary<string, object> { ["name"] = "John Smith" };
        var result = _renderer.Render("Hello {{ name }}", tokens);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Hello John Smith");
    }

    [Fact]
    public void Renders_nested_tokens()
    {
        var tokens = new Dictionary<string, object>
        {
            ["contact"] = new Dictionary<string, object>
            {
                ["name"] = "John Smith",
                ["title"] = "Program Manager"
            },
            ["agency"] = new Dictionary<string, object>
            {
                ["name"] = "VA"
            }
        };

        var template = "Dear {{ contact.name }}, RE: {{ agency.name }} opportunity";
        var result = _renderer.Render(template, tokens);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Dear John Smith, RE: VA opportunity");
    }

    [Fact]
    public void Handles_missing_token_gracefully()
    {
        var tokens = new Dictionary<string, object> { ["name"] = "John" };
        var result = _renderer.Render("Hello {{ name }}, your title is {{ title }}", tokens);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Hello John, your title is ");
    }

    [Fact]
    public void Renders_full_outreach_template()
    {
        var tokens = new Dictionary<string, object>
        {
            ["contact"] = new Dictionary<string, object>
            {
                ["first_name"] = "John",
                ["title"] = "Director of IT"
            },
            ["agency"] = new Dictionary<string, object>
            {
                ["name"] = "Department of Veterans Affairs"
            },
            ["opportunity"] = new Dictionary<string, object>
            {
                ["title"] = "Contact Center Modernization RFI",
                ["deadline"] = "March 15, 2026"
            },
            ["sequence"] = new Dictionary<string, object>
            {
                ["step"] = 1
            }
        };

        var template = """
            Dear {{ contact.first_name }},

            I noticed the {{ opportunity.title }} posted by {{ agency.name }}
            with a response deadline of {{ opportunity.deadline }}.

            Our team specializes in contact center solutions and we'd welcome
            the opportunity to discuss how we might support this initiative.

            Best regards,
            Tommy Shelburne
            """;

        var result = _renderer.Render(template, tokens);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Dear John");
        result.Value.Should().Contain("Contact Center Modernization RFI");
        result.Value.Should().Contain("Department of Veterans Affairs");
        result.Value.Should().Contain("March 15, 2026");
    }

    [Fact]
    public void Returns_error_for_invalid_template_syntax()
    {
        var tokens = new Dictionary<string, object>();
        var result = _renderer.Render("{% if %}", tokens);

        result.IsSuccess.Should().BeFalse();
    }
}
