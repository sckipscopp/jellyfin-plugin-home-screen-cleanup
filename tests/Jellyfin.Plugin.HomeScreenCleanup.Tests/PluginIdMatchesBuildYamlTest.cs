using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.HomeScreenCleanup.Tests;

public sealed class PluginIdMatchesBuildYamlTest
{
    private static readonly Regex GuidLine = new(
        "^guid:\\s*[\"']?(?<guid>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})[\"']?\\s*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void Plugin_id_matches_build_yaml_guid()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "build.yaml");
        Assert.True(File.Exists(path), "build.yaml was not copied to the test output: " + path);

        var match = GuidLine.Match(File.ReadAllText(path));

        Assert.True(match.Success, "No top-level guid: line in build.yaml.");
        Assert.Equal(PluginConstants.Id, Guid.Parse(match.Groups["guid"].Value, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Plugin_id_is_the_fixed_guid()
    {
        Assert.Equal(Guid.Parse("48774b8b-b1b2-49b0-b3f4-4ebaf867a696", CultureInfo.InvariantCulture), PluginConstants.Id);
        Assert.Equal("Home Screen Cleanup", PluginConstants.Name);
    }

    [Fact]
    public void Embedded_resources_are_present_under_the_expected_names()
    {
        var names = PluginConstants.Assembly.GetManifestResourceNames();

        Assert.Contains(PluginConstants.ClientScriptResourceName, names);
        Assert.Contains(PluginConstants.ConfigPageResourceName, names);
    }
}
