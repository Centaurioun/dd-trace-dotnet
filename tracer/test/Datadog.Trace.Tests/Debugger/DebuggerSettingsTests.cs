// <copyright file="DebuggerSettingsTests.cs" company="Datadog">
// Unless explicitly stated otherwise all files in this repository are licensed under the Apache 2 License.
// This product includes software developed at Datadog (https://www.datadoghq.com/). Copyright 2017 Datadog, Inc.
// </copyright>

using Datadog.Trace.Configuration;
using Datadog.Trace.Debugger;
using FluentAssertions;
using Xunit;

namespace Datadog.Trace.Tests.Debugger
{
    public class DebuggerSettingsTests
    {
        [Theory]
        [InlineData("-1")]
        [InlineData("0")]
        [InlineData("")]
        [InlineData(null)]
        public void InvalidMaxDepthToSerialize_DefaultUsed(string value)
        {
            var settings = new DebuggerSettings(new NameValueConfigurationSource(new()
            {
                { ConfigurationKeys.Debugger.MaxDepthToSerialize, value },
            }));

            settings.MaximumDepthOfMembersToCopy.Should().Be(1);
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("0")]
        [InlineData("")]
        [InlineData(null)]
        public void InvalidSerializationTimeThreshold_DefaultUsed(string value)
        {
            var settings = new DebuggerSettings(new NameValueConfigurationSource(new()
            {
                { ConfigurationKeys.Debugger.MaxTimeToSerialize, value },
            }));

            settings.MaxSerializationTimeInMilliseconds.Should().Be(150);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("false")]
        public void DebuggerDisabled(string enabled)
        {
            var settings = new DebuggerSettings(new NameValueConfigurationSource(new()
            {
                { ConfigurationKeys.Debugger.Enabled, enabled },
            }));

            settings.Enabled.Should().BeFalse();
        }

        [Fact]
        public void DebuggerSettings_UseSettings()
        {
            var settings = new DebuggerSettings(new NameValueConfigurationSource(new()
            {
                { ConfigurationKeys.Debugger.Enabled, "true" },
                { ConfigurationKeys.Debugger.MaxDepthToSerialize, "100" },
                { ConfigurationKeys.Debugger.MaxTimeToSerialize, "1000" },
            }));

            settings.Enabled.Should().BeTrue();
            settings.MaximumDepthOfMembersToCopy.Should().Be(100);
            settings.MaxSerializationTimeInMilliseconds.Should().Be(1000);
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("0")]
        [InlineData("")]
        [InlineData(null)]
        public void InvalidUploadBatchSize_DefaultUsed(string value)
        {
            var settings = new DebuggerSettings(new NameValueConfigurationSource(new()
            {
                { ConfigurationKeys.Debugger.UploadBatchSize, value },
            }));

            settings.UploadBatchSize.Should().Be(100);
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("0")]
        [InlineData("")]
        [InlineData(null)]
        public void InvalidDiagnosticsInterval_DefaultUsed(string value)
        {
            var settings = new DebuggerSettings(new NameValueConfigurationSource(new()
            {
                { ConfigurationKeys.Debugger.DiagnosticsInterval, value },
            }));

            settings.DiagnosticsIntervalSeconds.Should().Be(3600);
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("0")]
        [InlineData("")]
        [InlineData(null)]
        public void InvalidUploadFlushInterval_DefaultUsed(string value)
        {
            var settings = new DebuggerSettings(new NameValueConfigurationSource(new()
            {
                { ConfigurationKeys.Debugger.UploadFlushInterval, value },
            }));

            settings.UploadFlushIntervalMilliseconds.Should().Be(0);
        }
    }
}
