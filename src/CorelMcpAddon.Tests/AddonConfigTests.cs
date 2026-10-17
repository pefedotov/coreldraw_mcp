using Xunit;
using CorelMcp.Json;
using CorelMcp.Config;

namespace CorelMcpAddon.Tests
{
    /// <summary>Covers the explicit config.json mapping (no reflection, no third-party JSON).</summary>
    public class AddonConfigTests
    {
        [Fact]
        public void FromJson_ReadsEverySetting()
        {
            var json = JsonObject.Parse(
                "{\"Port\":9000,\"BindAddress\":\"127.0.0.1\",\"AutoStart\":false," +
                "\"AuthToken\":\"secret\",\"MaxBodyBytes\":1024,\"AllowedRoots\":\"C:\\\\out\"}");

            var config = AddonConfig.FromJson(json);

            Assert.Equal(9000, config.Port);
            Assert.Equal("127.0.0.1", config.BindAddress);
            Assert.False(config.AutoStart);
            Assert.Equal("secret", config.AuthToken);
            Assert.Equal(1024, config.MaxBodyBytes);
            Assert.Equal("C:\\out", config.AllowedRoots);
        }

        [Fact]
        public void FromJson_UsesDefaultsForMissingOrUnusableValues()
        {
            var config = AddonConfig.FromJson(JsonObject.Parse("{\"Port\":\"not a number\"}"));

            var defaults = new AddonConfig();
            Assert.Equal(defaults.Port, config.Port);
            Assert.Equal(defaults.BindAddress, config.BindAddress);
            Assert.True(config.AutoStart);
        }

        [Fact]
        public void FromJson_IsCaseInsensitiveForLenientConfigs()
        {
            var config = AddonConfig.FromJson(JsonObject.Parse("{\"port\":7000,\"autostart\":false}"));

            Assert.Equal(7000, config.Port);
            Assert.False(config.AutoStart);
        }

        [Fact]
        public void ToJson_RoundTripsThroughText()
        {
            var original = new AddonConfig
            {
                Port = 8123,
                BindAddress = "127.0.0.1",
                AutoStart = false,
                AuthToken = "token",
                MaxBodyBytes = 2048,
                AllowedRoots = "C:\\a;C:\\b"
            };

            var text = original.ToJson().ToString(JsonFormatting.Indented);
            var restored = AddonConfig.FromJson(JsonValue.Parse(text));

            Assert.Equal(original.Port, restored.Port);
            Assert.Equal(original.BindAddress, restored.BindAddress);
            Assert.Equal(original.AutoStart, restored.AutoStart);
            Assert.Equal(original.AuthToken, restored.AuthToken);
            Assert.Equal(original.MaxBodyBytes, restored.MaxBodyBytes);
            Assert.Equal(original.AllowedRoots, restored.AllowedRoots);
        }

        [Fact]
        public void DisabledTools_RoundTripsAndSplitsOnCommas()
        {
            var config = AddonConfig.FromJson(JsonObject.Parse("{\"DisabledTools\":\"a, b ,,c\"}"));

            Assert.Equal("a, b ,,c", config.DisabledTools);
            Assert.Equal(new[] { "a", "b", "c" }, config.DisabledToolNames());

            var restored = AddonConfig.FromJson(
                JsonValue.Parse(new AddonConfig { DisabledTools = "x,y" }.ToJson().ToString(JsonFormatting.Indented)));
            Assert.Equal("x,y", restored.DisabledTools);
            Assert.Empty(new AddonConfig().DisabledToolNames());
        }

        [Fact]
        public void ToJson_DoesNotPersistTheFilePath()
        {
            var json = new AddonConfig().ToJson();
            Assert.False(json.ContainsKey("ConfigPath"));
        }
    }
}
