// <copyright file="MsTestV2Tests.cs" company="Datadog">
// Unless explicitly stated otherwise all files in this repository are licensed under the Apache 2 License.
// This product includes software developed at Datadog (https://www.datadoghq.com/). Copyright 2017 Datadog, Inc.
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Datadog.Trace.Ci;
using Datadog.Trace.Ci.Tags;
using Datadog.Trace.TestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace Datadog.Trace.ClrProfiler.IntegrationTests.CI
{
    public class MsTestV2Tests : TestHelper
    {
        private const string TestSuiteName = "Samples.MSTestTests.TestSuite";
        private const string TestBundleName = "Samples.MSTestTests";

        public MsTestV2Tests(ITestOutputHelper output)
            : base("MSTestTests", output)
        {
            SetServiceVersion("1.0.0");
        }

        [SkippableTheory]
        [MemberData(nameof(PackageVersions.MSTest), MemberType = typeof(PackageVersions))]
        [Trait("Category", "EndToEnd")]
        [Trait("Category", "TestIntegrations")]
        public void SubmitTraces(string packageVersion)
        {
            var version = string.IsNullOrEmpty(packageVersion) ? new Version("2.2.8") : new Version(packageVersion);
            List<MockSpan> spans = null;
            var expectedSpanCount = version.CompareTo(new Version("2.2.5")) < 0 ? 13 : 15;

            try
            {
                SetEnvironmentVariable("DD_CIVISIBILITY_ENABLED", "1");
                SetEnvironmentVariable("DD_TRACE_DEBUG", "1");
                SetEnvironmentVariable("DD_DUMP_ILREWRITE_ENABLED", "1");

                using (var agent = EnvironmentHelper.GetMockAgent())
                using (ProcessResult processResult = RunDotnetTestSampleAndWaitForExit(agent, packageVersion: packageVersion))
                {
                    spans = agent.WaitForSpans(expectedSpanCount)
                        .Where(s => !(s.Tags.TryGetValue(Tags.InstrumentationName, out var sValue) && sValue == "HttpMessageHandler"))
                        .ToList();

                    // Check the span count
                    Assert.Equal(expectedSpanCount, spans.Count);

                    foreach (var targetSpan in spans)
                    {
                        // check the name
                        Assert.Equal("mstest.test", targetSpan.Name);

                        // check the CIEnvironmentValues decoration.
                        CheckCIEnvironmentValuesDecoration(targetSpan);

                        // check the runtime values
                        CheckRuntimeValues(targetSpan);

                        // check the bundle name
                        AssertTargetSpanEqual(targetSpan, TestTags.Bundle, TestBundleName);

                        // check the suite name
                        AssertTargetSpanEqual(targetSpan, TestTags.Suite, TestSuiteName);

                        // check the test type
                        AssertTargetSpanEqual(targetSpan, TestTags.Type, TestTags.TypeTest);

                        // check the test framework
                        AssertTargetSpanContains(targetSpan, TestTags.Framework, "MSTestV2");
                        Assert.True(targetSpan.Tags.Remove(TestTags.FrameworkVersion));

                        // check the version
                        AssertTargetSpanEqual(targetSpan, "version", "1.0.0");

                        // checks the runtime id tag
                        AssertTargetSpanExists(targetSpan, Tags.RuntimeId);

                        // checks the source tags
                        AssertTargetSpanExists(targetSpan, TestTags.SourceFile);

                        // checks code owners
                        AssertTargetSpanExists(targetSpan, TestTags.CodeOwners);

                        // checks the origin tag
                        CheckOriginTag(targetSpan);

                        // Check the Environment
                        AssertTargetSpanEqual(targetSpan, Tags.Env, "integration_tests");

                        // Language
                        AssertTargetSpanEqual(targetSpan, Tags.Language, TracerConstants.Language);

                        // CI Library Language
                        AssertTargetSpanEqual(targetSpan, CommonTags.LibraryVersion, TracerConstants.AssemblyVersion);

                        // check specific test span
                        switch (targetSpan.Tags[TestTags.Name])
                        {
                            case "SimplePassTest":
                                CheckSimpleTestSpan(targetSpan);
                                break;

                            case "SimpleSkipFromAttributeTest":
                                CheckSimpleSkipFromAttributeTest(targetSpan);
                                break;

                            case "SimpleErrorTest":
                                CheckSimpleErrorTest(targetSpan);
                                break;

                            case "TraitPassTest":
                                CheckSimpleTestSpan(targetSpan);
                                CheckTraitsValues(targetSpan);
                                break;

                            case "TraitSkipFromAttributeTest":
                                CheckSimpleSkipFromAttributeTest(targetSpan);
                                CheckTraitsValues(targetSpan);
                                break;

                            case "TraitErrorTest":
                                CheckSimpleErrorTest(targetSpan);
                                CheckTraitsValues(targetSpan);
                                break;

                            case "SimpleParameterizedTest":
                                CheckSimpleTestSpan(targetSpan);
                                AssertTargetSpanAnyOf(
                                    targetSpan,
                                    TestTags.Parameters,
                                    "{\"metadata\":{},\"arguments\":{\"xValue\":\"1\",\"yValue\":\"1\",\"expectedResult\":\"2\"}}",
                                    "{\"metadata\":{},\"arguments\":{\"xValue\":\"2\",\"yValue\":\"2\",\"expectedResult\":\"4\"}}",
                                    "{\"metadata\":{},\"arguments\":{\"xValue\":\"3\",\"yValue\":\"3\",\"expectedResult\":\"6\"}}");
                                break;

                            case "SimpleSkipParameterizedTest":
                                CheckSimpleSkipFromAttributeTest(targetSpan);
                                // On callsite the parameters tags are being sent with no parameters, this is not required due the whole test is skipped.
                                // That behavior has changed in calltarget.
                                AssertTargetSpanAnyOf(
                                    targetSpan,
                                    TestTags.Parameters,
                                    "{\"metadata\":{},\"arguments\":{\"xValue\":\"(default)\",\"yValue\":\"(default)\",\"expectedResult\":\"(default)\"}}");
                                break;

                            case "SimpleErrorParameterizedTest":
                                CheckSimpleErrorTest(targetSpan);
                                AssertTargetSpanAnyOf(
                                    targetSpan,
                                    TestTags.Parameters,
                                    "{\"metadata\":{},\"arguments\":{\"xValue\":\"1\",\"yValue\":\"0\",\"expectedResult\":\"2\"}}",
                                    "{\"metadata\":{},\"arguments\":{\"xValue\":\"2\",\"yValue\":\"0\",\"expectedResult\":\"4\"}}",
                                    "{\"metadata\":{},\"arguments\":{\"xValue\":\"3\",\"yValue\":\"0\",\"expectedResult\":\"6\"}}");
                                break;
                        }

                        // check remaining tag (only the name)
                        Assert.Single(targetSpan.Tags);
                    }
                }
            }
            catch
            {
                WriteSpans(spans);
                throw;
            }
        }

        private static void WriteSpans(List<MockSpan> spans)
        {
            if (spans is null || spans.Count == 0)
            {
                return;
            }

            Console.WriteLine("***********************************");

            int i = 0;
            foreach (var span in spans)
            {
                Console.Write($" {i++}) ");
                Console.Write($"TraceId={span.TraceId}, ");
                Console.Write($"SpanId={span.SpanId}, ");
                Console.Write($"Service={span.Service}, ");
                Console.Write($"Name={span.Name}, ");
                Console.Write($"Resource={span.Resource}, ");
                Console.Write($"Type={span.Type}, ");
                Console.Write($"Error={span.Error}");
                Console.WriteLine();
                Console.WriteLine($"   Tags=");
                foreach (var kv in span.Tags)
                {
                    Console.WriteLine($"       => {kv.Key} = {kv.Value}");
                }

                Console.WriteLine();
            }

            Console.WriteLine("***********************************");
        }

        private static void AssertTargetSpanAnyOf(MockSpan targetSpan, string key, params string[] values)
        {
            string actualValue = targetSpan.Tags[key];
            Assert.Contains(actualValue, values);
            targetSpan.Tags.Remove(key);
        }

        private static void AssertTargetSpanEqual(MockSpan targetSpan, string key, string value)
        {
            Assert.Equal(value, targetSpan.Tags[key]);
            targetSpan.Tags.Remove(key);
        }

        private static void AssertTargetSpanExists(MockSpan targetSpan, string key)
        {
            Assert.True(targetSpan.Tags.ContainsKey(key));
            targetSpan.Tags.Remove(key);
        }

        private static void AssertTargetSpanContains(MockSpan targetSpan, string key, string value)
        {
            Assert.Contains(value, targetSpan.Tags[key]);
            targetSpan.Tags.Remove(key);
        }

        private static void CheckCIEnvironmentValuesDecoration(MockSpan targetSpan)
        {
            var context = new SpanContext(null, null, null, null);
            var span = new Span(context, DateTimeOffset.UtcNow);
            CIEnvironmentValues.Instance.DecorateSpan(span);

            AssertEqual(CommonTags.CIProvider);
            AssertEqual(CommonTags.CIPipelineId);
            AssertEqual(CommonTags.CIPipelineName);
            AssertEqual(CommonTags.CIPipelineNumber);
            AssertEqual(CommonTags.CIPipelineUrl);
            AssertEqual(CommonTags.CIJobUrl);
            AssertEqual(CommonTags.CIJobName);
            AssertEqual(CommonTags.StageName);
            AssertEqual(CommonTags.CIWorkspacePath);
            AssertEqual(CommonTags.GitRepository);
            AssertEqual(CommonTags.GitCommit);
            AssertEqual(CommonTags.GitBranch);
            AssertEqual(CommonTags.GitTag);
            AssertEqual(CommonTags.GitCommitAuthorName);
            AssertEqual(CommonTags.GitCommitAuthorEmail);
            AssertEqual(CommonTags.GitCommitAuthorDate);
            AssertEqual(CommonTags.GitCommitCommitterName);
            AssertEqual(CommonTags.GitCommitCommitterEmail);
            AssertEqual(CommonTags.GitCommitCommitterDate);
            AssertEqual(CommonTags.GitCommitMessage);
            AssertEqual(CommonTags.BuildSourceRoot);

            void AssertEqual(string key)
            {
                if (span.GetTag(key) is not null)
                {
                    Assert.Equal(span.GetTag(key), targetSpan.Tags[key]);
                    targetSpan.Tags.Remove(key);
                }
            }
        }

        private static void CheckRuntimeValues(MockSpan targetSpan)
        {
            FrameworkDescription framework = FrameworkDescription.Instance;

            AssertTargetSpanEqual(targetSpan, CommonTags.RuntimeName, framework.Name);
            AssertTargetSpanEqual(targetSpan, CommonTags.RuntimeVersion, framework.ProductVersion);
            AssertTargetSpanEqual(targetSpan, CommonTags.RuntimeArchitecture, framework.ProcessArchitecture);
            AssertTargetSpanEqual(targetSpan, CommonTags.OSArchitecture, framework.OSArchitecture);
            AssertTargetSpanEqual(targetSpan, CommonTags.OSPlatform, framework.OSPlatform);
            AssertTargetSpanEqual(targetSpan, CommonTags.OSVersion, Environment.OSVersion.VersionString);
        }

        private static void CheckTraitsValues(MockSpan targetSpan)
        {
            // Check the traits tag value
            AssertTargetSpanEqual(targetSpan, TestTags.Traits, "{\"Category\":[\"Category01\"],\"Compatibility\":[\"Windows\",\"Linux\"]}");
        }

        private static void CheckOriginTag(MockSpan targetSpan)
        {
            // Check the test origin tag
            AssertTargetSpanEqual(targetSpan, Tags.Origin, TestTags.CIAppTestOriginName);
        }

        private static void CheckSimpleTestSpan(MockSpan targetSpan)
        {
            // Check the Test Status
            AssertTargetSpanEqual(targetSpan, TestTags.Status, TestTags.StatusPass);
        }

        private static void CheckSimpleSkipFromAttributeTest(MockSpan targetSpan)
        {
            // Check the Test Status
            AssertTargetSpanEqual(targetSpan, TestTags.Status, TestTags.StatusSkip);

            // Check the Test skip reason
            AssertTargetSpanEqual(targetSpan, TestTags.SkipReason, "Simple skip reason");
        }

        private static void CheckSimpleErrorTest(MockSpan targetSpan)
        {
            // Check the Test Status
            AssertTargetSpanEqual(targetSpan, TestTags.Status, TestTags.StatusFail);

            // Check the span error flag
            Assert.Equal(1, targetSpan.Error);

            // Check the error type
            AssertTargetSpanEqual(targetSpan, Tags.ErrorType, typeof(DivideByZeroException).FullName);

            // Check the error stack
            AssertTargetSpanContains(targetSpan, Tags.ErrorStack, typeof(DivideByZeroException).FullName);

            // Check the error message
            AssertTargetSpanEqual(targetSpan, Tags.ErrorMsg, new DivideByZeroException().Message);
        }
    }
}
