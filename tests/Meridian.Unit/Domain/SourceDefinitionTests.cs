using FluentAssertions;
using Meridian.Domain.Sources;

namespace Meridian.Unit.Domain;

public class SourceDefinitionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_initializes_enabled_and_never_run()
    {
        var s = SourceDefinition.Create(TenantId, SourceAdapterType.SamGov, "SAM", "{}");

        s.IsEnabled.Should().BeTrue();
        s.LastRunStatus.Should().Be(SourceRunStatus.NeverRun);
        s.ConsecutiveFailures.Should().Be(0);
        s.TenantId.Should().Be(TenantId);
        s.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_requires_tenant_and_name()
    {
        FluentActions.Invoking(() => SourceDefinition.Create(Guid.Empty, SourceAdapterType.SamGov, "n", "{}"))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SourceDefinition.Create(TenantId, SourceAdapterType.SamGov, " ", "{}"))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_defaults_empty_parameters_to_object_literal()
    {
        var s = SourceDefinition.Create(TenantId, SourceAdapterType.SamGov, "SAM", "");
        s.ParametersJson.Should().Be("{}");
    }

    [Fact]
    public void MarkRunSucceeded_resets_failure_counter()
    {
        var s = SourceDefinition.Create(TenantId, SourceAdapterType.SamGov, "SAM", "{}");
        s.MarkRunStarted();
        s.MarkRunFailed("boom");
        s.MarkRunFailed("boom");
        s.ConsecutiveFailures.Should().Be(2);

        s.MarkRunSucceeded();

        s.ConsecutiveFailures.Should().Be(0);
        s.LastRunStatus.Should().Be(SourceRunStatus.Succeeded);
        s.LastRunError.Should().BeNull();
    }

    [Fact]
    public void MarkRunFailed_auto_disables_after_threshold()
    {
        var s = SourceDefinition.Create(TenantId, SourceAdapterType.SamGov, "SAM", "{}");
        for (var i = 0; i < SourceDefinition.AutoDisableAfterConsecutiveFailures; i++)
            s.MarkRunFailed("boom");

        s.IsEnabled.Should().BeFalse();
        s.LastRunStatus.Should().Be(SourceRunStatus.Disabled);
    }

    [Fact]
    public void Enable_clears_failure_counter_and_resets_status_from_disabled()
    {
        var s = SourceDefinition.Create(TenantId, SourceAdapterType.SamGov, "SAM", "{}");
        for (var i = 0; i < SourceDefinition.AutoDisableAfterConsecutiveFailures; i++)
            s.MarkRunFailed("boom");

        s.Enable();

        s.IsEnabled.Should().BeTrue();
        s.ConsecutiveFailures.Should().Be(0);
        s.LastRunStatus.Should().Be(SourceRunStatus.NeverRun);
    }

    [Fact]
    public void Disable_sets_status_to_Disabled()
    {
        var s = SourceDefinition.Create(TenantId, SourceAdapterType.GenericRss, "feed", "{}");
        s.Disable();

        s.IsEnabled.Should().BeFalse();
        s.LastRunStatus.Should().Be(SourceRunStatus.Disabled);
    }

    [Fact]
    public void UpdateParameters_rejects_empty_json()
    {
        var s = SourceDefinition.Create(TenantId, SourceAdapterType.SamGov, "SAM", "{}");
        FluentActions.Invoking(() => s.UpdateParameters(""))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateParameters_changes_parameters_json()
    {
        var s = SourceDefinition.Create(TenantId, SourceAdapterType.SamGov, "SAM", "{}");
        s.UpdateParameters("{\"k\":1}");
        s.ParametersJson.Should().Be("{\"k\":1}");
    }

    [Fact]
    public void Rename_trims_and_updates_name()
    {
        var s = SourceDefinition.Create(TenantId, SourceAdapterType.SamGov, "SAM", "{}");
        s.Rename("  New name  ");
        s.Name.Should().Be("New name");
    }
}
