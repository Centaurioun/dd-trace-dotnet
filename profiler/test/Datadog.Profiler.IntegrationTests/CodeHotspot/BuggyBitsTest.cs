// <copyright file="BuggyBitsTest.cs" company="Datadog">
// Unless explicitly stated otherwise all files in this repository are licensed under the Apache 2 License.
// This product includes software developed at Datadog (https://www.datadoghq.com/). Copyright 2022 Datadog, Inc.
// </copyright>

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Datadog.Profiler.IntegrationTests.Helpers;
using Datadog.Profiler.SmokeTests;
using Datadog.Trace;
using Datadog.Trace.TestHelpers;
using FluentAssertions;
using MessagePack;
using Perftools.Profiles;
using Xunit;
using Xunit.Abstractions;

namespace Datadog.Profiler.IntegrationTests.CodeHotspot
{
    public class BuggyBitsTest
    {
        private static readonly Regex RuntimeIdPattern = new("runtime-id:(?<runtimeId>[A-Z0-9-]+)", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);
        private readonly ITestOutputHelper _output;

        public BuggyBitsTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [TestAppFact("Samples.BuggyBits")]
        public void CheckSpanContextAreAttached(string appName, string framework, string appAssembly)
        {
            var runner = new TestApplicationRunner(appName, framework, appAssembly, _output, enableTracer: true, commandLine: "--scenario 1");
            // By default, the codehotspot feature is activated

            using var agent = new MockDatadogAgent(_output);

            var profilerRuntimeIds = new List<string>();
            agent.ProfilerRequestReceived += (object sender, EventArgs<HttpListenerContext> ctx) =>
            {
                profilerRuntimeIds.Add(ExtractRuntimeIdFromProfilerRequest(ctx.Value.Request));
            };

            var tracerRuntimeIds = new List<string>();
            agent.TracerRequestReceived += (object sender, EventArgs<HttpListenerContext> ctx) =>
            {
                tracerRuntimeIds.AddRange(ExtractRuntimeIdsFromTracerRequest(ctx.Value.Request));
            };

            runner.Run(agent);

            Assert.True(agent.NbCallsOnProfilingEndpoint > 0);

            Assert.Single(profilerRuntimeIds.Distinct());
            Assert.Single(tracerRuntimeIds.Distinct());

            var profilerRuntimeId = profilerRuntimeIds.First();
            Assert.NotNull(profilerRuntimeId);
            Assert.NotEmpty(profilerRuntimeId);

            var tracerRuntimeId = tracerRuntimeIds.First();
            Assert.NotNull(tracerRuntimeId);
            Assert.NotEmpty(tracerRuntimeId);

            Assert.Equal(profilerRuntimeId, tracerRuntimeId);

            var profileTracingContexts = GetTracingContextsFromPprofFiles(runner.Environment.PprofDir);
            Assert.NotEmpty(profileTracingContexts);

            // In the first versions of this test, we extracted span ids from Tracer requests and ensured that the ones collected by the
            // profiler was a subset. But this makes the test flacky: not flushed when the application is closing.
        }

        [TestAppFact("Samples.BuggyBits")]
        public void NoTraceContextAttachedIfFeatureDeactivated(string appName, string framework, string appAssembly)
        {
            var runner = new TestApplicationRunner(appName, framework, appAssembly, _output, enableTracer: true, commandLine: "--scenario 1");
            runner.Environment.SetVariable(EnvironmentVariables.CodeHotSpotsEnable, "0");

            using var agent = new MockDatadogAgent(_output);

            runner.Run(agent);

            Assert.True(agent.NbCallsOnProfilingEndpoint > 0);

            var tracingContexts = GetTracingContextsFromPprofFiles(runner.Environment.PprofDir);
            Assert.Empty(tracingContexts);
        }

        [TestAppFact("Samples.BuggyBits")]
        public void CheckEndpointsAreAttached(string appName, string framework, string appAssembly)
        {
            var runner = new TestApplicationRunner(appName, framework, appAssembly, _output, enableTracer: true, commandLine: "--scenario 1");
            runner.TestDurationInSeconds = 20;

            // By default, the endpoint profiling feature is activated

            using var agent = new MockDatadogAgent(_output);

            runner.Run(agent);

            Assert.True(agent.NbCallsOnProfilingEndpoint > 0);

            var endpoints = GetEndpointsFromPprofFiles(runner.Environment.PprofDir);

            endpoints.Distinct().Should().BeEquivalentTo("GET /products/index");
        }

        [TestAppFact("Samples.BuggyBits")]
        public void NoEndpointsAttachedIfFeatureDeactivated(string appName, string framework, string appAssembly)
        {
            var runner = new TestApplicationRunner(appName, framework, appAssembly, _output, enableTracer: true, commandLine: "--scenario 1");
            runner.TestDurationInSeconds = 20;
            runner.Environment.SetVariable(EnvironmentVariables.EndpointProfilerEnabled, "0");

            using var agent = new MockDatadogAgent(_output);

            runner.Run(agent);

            Assert.True(agent.NbCallsOnProfilingEndpoint > 0);

            var endpoints = GetEndpointsFromPprofFiles(runner.Environment.PprofDir).Distinct();

            endpoints.Should().BeEmpty();
        }

        private static HashSet<string> ExtractRuntimeIdsFromTracerRequest(HttpListenerRequest request)
        {
            var traces = MessagePackSerializer.Deserialize<List<List<MockSpan>>>(request.InputStream);

            var runtimeIds = new HashSet<string>();
            foreach (var trace in traces)
            {
                foreach (var span in trace)
                {
                    var currentRuntimeId = string.Empty;
                    if (span.Tags?.TryGetValue(Tags.RuntimeId, out currentRuntimeId) ?? false)
                    {
                        runtimeIds.Add(currentRuntimeId);
                    }
                }
            }

            return runtimeIds;
        }

        private static string ExtractRuntimeIdFromProfilerRequest(HttpListenerRequest request)
        {
            string text;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                text = reader.ReadToEnd();
            }

            var match = RuntimeIdPattern.Match(text);
            return match.Groups["runtimeId"].Value;
        }

        private static IEnumerable<string> GetEndpointsFromPprofFiles(string pprofDir)
        {
            foreach (var file in Directory.EnumerateFiles(pprofDir, "*.pprof", SearchOption.AllDirectories))
            {
                using var s = File.OpenRead(file);
                var profile = Profile.Parser.ParseFrom(s);

                foreach (var label in profile.Labels().SelectMany(_ => _))
                {
                    if (label.Name == "trace endpoint")
                    {
                        yield return label.Value;
                    }
                }
            }
        }

        private static List<(ulong LocalRootSpanId, ulong SpanId)> GetTracingContextsFromPprofFiles(string pprofDir)
        {
            var tracingContext = new List<(ulong LocalRootSpanId, ulong SpanId)>();
            foreach (var file in Directory.EnumerateFiles(pprofDir, "*.pprof", SearchOption.AllDirectories))
            {
                tracingContext.AddRange(ExtractTracingContext(file));
            }

            return tracingContext;
        }

        private static IEnumerable<(ulong LocalRootSpanId, ulong SpanId)> ExtractTracingContext(string file)
        {
            using var s = File.OpenRead(file);
            var profile = Profile.Parser.ParseFrom(s);

            foreach (var labelsPerSample in profile.Labels())
            {
                ulong localRootSpanId = 0;
                ulong spanId = 0;

                foreach (var label in labelsPerSample)
                {
                    if (label.Name == "local root span id")
                    {
                        localRootSpanId = ulong.Parse(label.Value);
                    }

                    if (label.Name == "span id")
                    {
                        spanId = ulong.Parse(label.Value);
                    }
                }

                if (spanId != 0 && localRootSpanId != 0)
                {
                    yield return (localRootSpanId, spanId);
                }
            }
        }
    }
}
