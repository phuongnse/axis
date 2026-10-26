using Axis.Admin;

namespace Axis.Integration.Tests;

public sealed class AdminCommandTableTests
{
    [Fact]
    public void Every_command_of_the_table_is_marked_and_the_marks_are_the_agreed_ones()
    {
        var commands = AdminCommands.Commands;

        Assert.DoesNotContain(commands, command => command.Audit == AuditMark.Unmarked);
        Assert.Equal(commands.Count, commands.Select(command => command.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            new Dictionary<string, AuditMark>
            {
                ["tenant create"] = AuditMark.OnePerTenant,
                ["tenant backup"] = AuditMark.OnePerTenant,
                ["tenant restore"] = AuditMark.OnePerTenant,
                ["tenant suspend"] = AuditMark.OnePerTenant,
                ["tenant resume"] = AuditMark.OnePerTenant,
                ["tenant rotate-passwords"] = AuditMark.OnePerTenant,
                ["migrate"] = AuditMark.OnePerTenant,
                ["activate"] = AuditMark.OnePerTenant,
                ["tenant binding set"] = AuditMark.OnePerTenant,
                ["tenant binding remove"] = AuditMark.OnePerTenant,
                ["tenant host add"] = AuditMark.OnePerTenant,
                ["tenant host remove"] = AuditMark.OnePerTenant,
                ["tenant host move"] = AuditMark.OnePerTenant,
                ["platform rekey"] = AuditMark.OnePerRun,
                ["tenant list"] = AuditMark.None,
                ["tenant binding list"] = AuditMark.None,
                ["version"] = AuditMark.None,
                ["publisher"] = AuditMark.None,
            }.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            commands.ToDictionary(command => command.Name, command => command.Audit).OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }
}
