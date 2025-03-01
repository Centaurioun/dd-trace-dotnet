// <copyright file="TestHelper.cs" company="Datadog">
// Unless explicitly stated otherwise all files in this repository are licensed under the Apache 2 License.
// This product includes software developed at Datadog (https://www.datadoghq.com/). Copyright 2017 Datadog, Inc.
// </copyright>

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Datadog.Trace.Configuration;
using Datadog.Trace.Debugger.Helpers;
using Datadog.Trace.RemoteConfigurationManagement.Protocol;
using Datadog.Trace.RemoteConfigurationManagement.Protocol.Tuf;
using Datadog.Trace.Vendors.Newtonsoft.Json;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

#if NETFRAMEWORK
using Datadog.Trace.ExtensionMethods; // needed for Dictionary<K,V>.GetValueOrDefault()
#endif

namespace Datadog.Trace.TestHelpers
{
    public abstract class TestHelper : IDisposable
    {
        protected TestHelper(string sampleAppName, string samplePathOverrides, ITestOutputHelper output)
            : this(new EnvironmentHelper(sampleAppName, typeof(TestHelper), output, samplePathOverrides), output)
        {
        }

        protected TestHelper(string sampleAppName, string samplePathOverrides, ITestOutputHelper output, bool prependSamplesToAppName)
            : this(new EnvironmentHelper(sampleAppName, typeof(TestHelper), output, samplePathOverrides, prependSamplesToAppName: false), output)
        {
        }

        protected TestHelper(string sampleAppName, ITestOutputHelper output)
            : this(new EnvironmentHelper(sampleAppName, typeof(TestHelper), output), output)
        {
        }

        protected TestHelper(EnvironmentHelper environmentHelper, ITestOutputHelper output)
        {
            EnvironmentHelper = environmentHelper;
            Output = output;

            Output.WriteLine($"Platform: {EnvironmentTools.GetPlatform()}");
            Output.WriteLine($"Configuration: {EnvironmentTools.GetBuildConfiguration()}");
            Output.WriteLine($"TargetFramework: {EnvironmentHelper.GetTargetFramework()}");
            Output.WriteLine($".NET Core: {EnvironmentHelper.IsCoreClr()}");
            Output.WriteLine($"Native Loader DLL: {EnvironmentHelper.GetNativeLoaderPath()}");
        }

        public bool SecurityEnabled { get; private set; }

        protected EnvironmentHelper EnvironmentHelper { get; }

        protected string TestPrefix => $"{EnvironmentTools.GetBuildConfiguration()}.{EnvironmentHelper.GetTargetFramework()}";

        protected ITestOutputHelper Output { get; }

        public virtual void Dispose()
        {
        }

        public Process StartDotnetTestSample(MockTracerAgent agent, string arguments, string packageVersion, int aspNetCorePort, string framework = "")
        {
            // get path to sample app that the profiler will attach to
            string sampleAppPath = EnvironmentHelper.GetTestCommandForSampleApplicationPath(packageVersion, framework);
            if (!File.Exists(sampleAppPath))
            {
                throw new Exception($"application not found: {sampleAppPath}");
            }

            Output.WriteLine($"Starting Application: {sampleAppPath}");
            string testCli = EnvironmentHelper.GetDotNetTest();
            string exec = testCli;
            string appPath = testCli.StartsWith("dotnet") ? $"vstest {sampleAppPath}" : sampleAppPath;
            Output.WriteLine("Executable: " + exec);
            Output.WriteLine("ApplicationPath: " + appPath);
            return ProfilerHelper.StartProcessWithProfiler(
                exec,
                EnvironmentHelper,
                agent,
                $"{appPath} {arguments ?? string.Empty}",
                aspNetCorePort: aspNetCorePort,
                processToProfile: exec + ";testhost.exe");
        }

        public ProcessResult RunDotnetTestSampleAndWaitForExit(MockTracerAgent agent, string arguments = null, string packageVersion = "", string framework = "")
        {
            var process = StartDotnetTestSample(agent, arguments, packageVersion, aspNetCorePort: 5000, framework: framework);

            using var helper = new ProcessHelper(process);

            process.WaitForExit();
            helper.Drain();
            var exitCode = process.ExitCode;

            Output.WriteLine($"ProcessId: " + process.Id);
            Output.WriteLine($"Exit Code: " + exitCode);

            var standardOutput = helper.StandardOutput;

            if (!string.IsNullOrWhiteSpace(standardOutput))
            {
                Output.WriteLine($"StandardOutput:{Environment.NewLine}{standardOutput}");
            }

            var standardError = helper.ErrorOutput;

            if (!string.IsNullOrWhiteSpace(standardError))
            {
                Output.WriteLine($"StandardError:{Environment.NewLine}{standardError}");
            }

            return new ProcessResult(process, standardOutput, standardError, exitCode);
        }

        public Process StartSample(MockTracerAgent agent, string arguments, string packageVersion, int aspNetCorePort, string framework = "")
        {
            // get path to sample app that the profiler will attach to
            var sampleAppPath = EnvironmentHelper.GetSampleApplicationPath(packageVersion, framework);
            if (!File.Exists(sampleAppPath))
            {
                throw new Exception($"application not found: {sampleAppPath}");
            }

            Output.WriteLine($"Starting Application: {sampleAppPath}");
            var executable = EnvironmentHelper.IsCoreClr() ? EnvironmentHelper.GetSampleExecutionSource() : sampleAppPath;
            var args = EnvironmentHelper.IsCoreClr() ? $"{sampleAppPath} {arguments ?? string.Empty}" : arguments;

            return ProfilerHelper.StartProcessWithProfiler(
                executable,
                EnvironmentHelper,
                agent,
                args,
                aspNetCorePort: aspNetCorePort,
                processToProfile: executable);
        }

        public async Task<bool> TakeMemoryDump(Process process)
        {
            try
            {
                if (EnvironmentTools.IsLinux())
                {
                    var dotnetRuntimeFolder = Path.GetDirectoryName(typeof(object).Assembly.Location);
                    var createDumpExecutable = Path.Combine(dotnetRuntimeFolder!, "createdump");
                    // createdump automatically puts the dump in the /tmp/ directory, which is where we grab it from
                    var args = process.Id.ToString();
                    return CaptureMemoryDump(createDumpExecutable, args);
                }
                else if (EnvironmentTools.IsWindows())
                {
                    var procDumpExecutable = await DownloadProcdumpZipAndExtract();
                    var args = $"-ma {process.Id} -accepteula";
                    return CaptureMemoryDump(procDumpExecutable, args);
                }
            }
            catch (Exception ex)
            {
                Output.WriteLine("Error taking memory dump: " + ex);
                return false;
            }

            return false;
        }

        public ProcessResult RunSampleAndWaitForExit(MockTracerAgent agent, string arguments = null, string packageVersion = "", string framework = "", int aspNetCorePort = 5000)
        {
            var process = StartSample(agent, arguments, packageVersion, aspNetCorePort: aspNetCorePort, framework: framework);

            using var helper = new ProcessHelper(process);

            // this is _way_ too long, but we want to be v. safe
            // the goal is just to make sure we kill the test before
            // the whole CI run times out
            var timeoutMs = (int)TimeSpan.FromMinutes(10).TotalMilliseconds;
            var ranToCompletion = process.WaitForExit(timeoutMs) && helper.Drain(timeoutMs / 2);

            if (!ranToCompletion && !process.HasExited)
            {
                var tookMemoryDump = TakeMemoryDump(process);
                process.Kill();
                throw new Exception($"The sample did not exit in {timeoutMs}ms. Memory dump taken: {tookMemoryDump.Result}. Killing process.");
            }

            var exitCode = process.ExitCode;

            Output.WriteLine($"ProcessId: " + process.Id);
            Output.WriteLine($"Exit Code: " + exitCode);

            var standardOutput = helper.StandardOutput;

            if (!string.IsNullOrWhiteSpace(standardOutput))
            {
                Output.WriteLine($"StandardOutput:{Environment.NewLine}{standardOutput}");
            }

            var standardError = helper.ErrorOutput;

            if (!string.IsNullOrWhiteSpace(standardError))
            {
                Output.WriteLine($"StandardError:{Environment.NewLine}{standardError}");
            }

#if NETCOREAPP2_1
            if (exitCode == 139)
            {
                // Segmentation faults are expected on .NET Core because of a bug in the runtime: https://github.com/dotnet/runtime/issues/11885
                throw new SkipException("Segmentation fault on .NET Core 2.1");
            }
#endif
            if (exitCode == 134
             && standardError?.Contains("System.Threading.AbandonedMutexException: The wait completed due to an abandoned mutex") == true
             && standardError?.Contains("Coverlet.Core.Instrumentation.Tracker") == true)
            {
                // Coverlet occasionally throws AbandonedMutexException during clean up
                throw new SkipException("Coverlet threw AbandonedMutexException during cleanup");
            }

            exitCode.Should().Be(0);

            return new ProcessResult(process, standardOutput, standardError, exitCode);
        }

        public (Process Process, string ConfigFile) StartIISExpress(MockTracerAgent agent, int iisPort, IisAppType appType, string subAppPath)
        {
            var iisExpress = EnvironmentHelper.GetIisExpressPath();

            var appPool = appType switch
            {
                IisAppType.AspNetClassic => "Clr4ClassicAppPool",
                IisAppType.AspNetIntegrated => "Clr4IntegratedAppPool",
                IisAppType.AspNetCoreInProcess => "UnmanagedClassicAppPool",
                IisAppType.AspNetCoreOutOfProcess => "UnmanagedClassicAppPool",
                _ => throw new InvalidOperationException($"Unknown {nameof(IisAppType)} '{appType}'"),
            };

            var appPath = appType switch
            {
                IisAppType.AspNetClassic => EnvironmentHelper.GetSampleProjectDirectory(),
                IisAppType.AspNetIntegrated => EnvironmentHelper.GetSampleProjectDirectory(),
                IisAppType.AspNetCoreInProcess => EnvironmentHelper.GetSampleApplicationOutputDirectory(),
                IisAppType.AspNetCoreOutOfProcess => EnvironmentHelper.GetSampleApplicationOutputDirectory(),
                _ => throw new InvalidOperationException($"Unknown {nameof(IisAppType)} '{appType}'"),
            };

            var configTemplate = File.ReadAllText("applicationHost.config");

            var newConfig = Path.GetTempFileName();

            var virtualAppSection = subAppPath switch
            {
                null or "" or "/" => string.Empty,
                _ when !subAppPath.StartsWith("/") => throw new ArgumentException("Application path must start with '/'", nameof(subAppPath)),
                _ when subAppPath.EndsWith("/") => throw new ArgumentException("Application path must not end with '/'", nameof(subAppPath)),
                _ => $"<application path=\"{subAppPath}\" applicationPool=\"{appPool}\"><virtualDirectory path=\"/\" physicalPath=\"{appPath}\" /></application>",
            };

            configTemplate = configTemplate
                            .Replace("[PATH]", appPath)
                            .Replace("[PORT]", iisPort.ToString())
                            .Replace("[POOL]", appPool)
                            .Replace("[VIRTUAL_APPLICATION]", virtualAppSection);

            var isAspNetCore = appType == IisAppType.AspNetCoreInProcess || appType == IisAppType.AspNetCoreOutOfProcess;
            if (isAspNetCore)
            {
                var hostingModel = appType == IisAppType.AspNetCoreInProcess ? "inprocess" : "outofprocess";
                configTemplate = configTemplate
                                .Replace("[DOTNET]", EnvironmentHelper.GetDotnetExe())
                                .Replace("[RELATIVE_SAMPLE_PATH]", $".\\{EnvironmentHelper.GetSampleApplicationFileName()}")
                                .Replace("[HOSTING_MODEL]", hostingModel);
            }

            File.WriteAllText(newConfig, configTemplate);

            var args = new[]
                {
                    "/site:sample",
                    $"/config:{newConfig}",
                    "/systray:false",
                    "/trace:info"
                };

            Output.WriteLine($"[webserver] starting {iisExpress} {string.Join(" ", args)}");

            var process = ProfilerHelper.StartProcessWithProfiler(
                iisExpress,
                EnvironmentHelper,
                agent,
                arguments: string.Join(" ", args),
                redirectStandardInput: true,
                processToProfile: appType == IisAppType.AspNetCoreOutOfProcess ? "dotnet.exe" : iisExpress);

            var wh = new EventWaitHandle(false, EventResetMode.AutoReset);

            Task.Run(() =>
            {
                string line;
                while ((line = process.StandardOutput.ReadLine()) != null)
                {
                    Output.WriteLine($"[webserver][stdout] {line}");

                    if (line.Contains("IIS Express is running"))
                    {
                        wh.Set();
                    }
                }
            });

            Task.Run(() =>
            {
                string line;
                while ((line = process.StandardError.ReadLine()) != null)
                {
                    Output.WriteLine($"[webserver][stderr] {line}");
                }
            });

            wh.WaitOne(5000);

            // Wait for iis express to finish starting up
            var retries = 5;
            while (true)
            {
                var usedPorts = IPGlobalProperties.GetIPGlobalProperties()
                                                  .GetActiveTcpListeners()
                                                  .Select(ipEndPoint => ipEndPoint.Port);

                if (usedPorts.Contains(iisPort))
                {
                    break;
                }

                retries--;

                if (retries == 0)
                {
                    throw new Exception("Gave up waiting for IIS Express.");
                }

                Thread.Sleep(1500);
            }

            return (process, newConfig);
        }

        public void SetEnvironmentVariable(string key, string value)
        {
            EnvironmentHelper.CustomEnvironmentVariables[key] = value;
        }

        protected void ValidateSpans<T>(IEnumerable<MockSpan> spans, Func<MockSpan, T> mapper, IEnumerable<T> expected)
        {
            var spanLookup = new Dictionary<T, int>();
            foreach (var span in spans)
            {
                var key = mapper(span);
                if (spanLookup.ContainsKey(key))
                {
                    spanLookup[key]++;
                }
                else
                {
                    spanLookup[key] = 1;
                }
            }

            var missing = new List<T>();
            foreach (var e in expected)
            {
                var found = spanLookup.ContainsKey(e);
                if (found)
                {
                    if (--spanLookup[e] <= 0)
                    {
                        spanLookup.Remove(e);
                    }
                }
                else
                {
                    missing.Add(e);
                }
            }

            foreach (var e in missing)
            {
                Assert.True(false, $"no span found for `{e}`, remaining spans: `{string.Join(", ", spanLookup.Select(kvp => $"{kvp.Key}").ToArray())}`");
            }
        }

        protected void EnableDebugMode()
        {
            EnvironmentHelper.DebugModeEnabled = true;
        }

        protected void SetServiceVersion(string serviceVersion)
        {
            SetEnvironmentVariable("DD_VERSION", serviceVersion);
        }

        protected void SetSecurity(bool security)
        {
            SecurityEnabled = security;
            SetEnvironmentVariable(Configuration.ConfigurationKeys.AppSec.Enabled, security ? "true" : "false");
        }

        protected void SetInstrumentationVerification()
        {
            bool verificationEnabled = ShouldUseInstrumentationVerification();
            SetEnvironmentVariable(InstrumentationVerification.InstrumentationVerificationEnabled, verificationEnabled ? "1" : "0");
            SetEnvironmentVariable(Configuration.ConfigurationKeys.LogDirectory, verificationEnabled ? EnvironmentHelper.LogDirectory : null);
        }

        protected void VerifyInstrumentation(Process process)
        {
            if (!ShouldUseInstrumentationVerification())
            {
                return;
            }

            var logDirectory = EnvironmentHelper.LogDirectory;
            InstrumentationVerification.VerifyInstrumentation(process, logDirectory);
        }

        protected bool ShouldUseInstrumentationVerification()
        {
            if (!EnvironmentTools.IsWindows())
            {
                // Instrumentation Verification is currently only supported only on Windows
                return false;
            }

            // verify instrumentation adds a lot of time to tests so we only run it on azure and if it a scheduled build.
            // Return 'true' to verify instrumentation on local machine.
            // return true;
            return EnvironmentHelper.IsRunningInAzureDevOps() && EnvironmentHelper.IsScheduledBuild();
        }

        protected void EnableDirectLogSubmission(int intakePort, string integrationName, string host = "integration_tests")
        {
            SetEnvironmentVariable(ConfigurationKeys.DirectLogSubmission.Host, host);
            SetEnvironmentVariable(ConfigurationKeys.DirectLogSubmission.Url, $"http://127.0.0.1:{intakePort}");
            SetEnvironmentVariable(ConfigurationKeys.DirectLogSubmission.EnabledIntegrations, integrationName);
            SetEnvironmentVariable(ConfigurationKeys.ApiKey, "DUMMY_KEY_REQUIRED_FOR_DIRECT_SUBMISSION");
        }

        protected void EnableTelemetry(bool enabled = true, int? standaloneAgentPort = null)
        {
            SetEnvironmentVariable("DD_INSTRUMENTATION_TELEMETRY_ENABLED", enabled.ToString());
            SetEnvironmentVariable("DD_INSTRUMENTATION_TELEMETRY_AGENTLESS_ENABLED", standaloneAgentPort.HasValue.ToString());

            if (standaloneAgentPort.HasValue)
            {
                SetEnvironmentVariable("DD_INSTRUMENTATION_TELEMETRY_URL", $"http://localhost:{standaloneAgentPort}");
                // API key is required for agentless
                SetEnvironmentVariable("DD_API_KEY", "INVALID_KEY_FOR_TESTS");
            }
        }

        protected async Task<IImmutableList<MockSpan>> GetWebServerSpans(
            string path,
            MockTracerAgent agent,
            int httpPort,
            HttpStatusCode expectedHttpStatusCode,
            int expectedSpanCount = 2,
            bool filterServerSpans = true)
        {
            using var httpClient = new HttpClient();

            // disable tracing for this HttpClient request
            httpClient.DefaultRequestHeaders.Add(HttpHeaderNames.TracingEnabled, "false");
            httpClient.DefaultRequestHeaders.Add(HttpHeaderNames.UserAgent, "testhelper");
            var testStart = DateTime.UtcNow;
            var response = await httpClient.GetAsync($"http://localhost:{httpPort}" + path);
            var content = await response.Content.ReadAsStringAsync();
            Output.WriteLine($"[http] {response.StatusCode} {content}");
            Assert.Equal(expectedHttpStatusCode, response.StatusCode);

            if (filterServerSpans)
            {
                agent.SpanFilters.Add(IsServerSpan);
            }

            return agent.WaitForSpans(
                count: expectedSpanCount,
                minDateTime: testStart,
                returnAllOperations: true);
        }

        protected async Task AssertWebServerSpan(
            string path,
            MockTracerAgent agent,
            int httpPort,
            HttpStatusCode expectedHttpStatusCode,
            bool isError,
            string expectedAspNetErrorType,
            string expectedAspNetErrorMessage,
            string expectedErrorType,
            string expectedErrorMessage,
            string expectedSpanType,
            string expectedOperationName,
            string expectedAspNetResourceName,
            string expectedResourceName,
            string expectedServiceVersion,
            SerializableDictionary expectedTags = null)
        {
            IImmutableList<MockSpan> spans;

            using (var httpClient = new HttpClient())
            {
                // disable tracing for this HttpClient request
                httpClient.DefaultRequestHeaders.Add(HttpHeaderNames.TracingEnabled, "false");
                var testStart = DateTime.UtcNow;
                var response = await httpClient.GetAsync($"http://localhost:{httpPort}" + path);
                var content = await response.Content.ReadAsStringAsync();
                Output.WriteLine($"[http] {response.StatusCode} {content}");
                Assert.Equal(expectedHttpStatusCode, response.StatusCode);

                agent.SpanFilters.Add(IsServerSpan);

                spans = agent.WaitForSpans(
                    count: 2,
                    minDateTime: testStart,
                    returnAllOperations: true);

                Assert.True(spans.Count == 2, $"expected two span, saw {spans.Count}");
            }

            var aspnetSpan = spans.FirstOrDefault(s => s.Name == "aspnet.request");
            var innerSpan = spans.FirstOrDefault(s => s.Name == expectedOperationName);

            Assert.NotNull(aspnetSpan);
            Assert.Equal(expectedAspNetResourceName, aspnetSpan.Resource);

            Assert.NotNull(innerSpan);
            Assert.Equal(expectedResourceName, innerSpan.Resource);

            foreach (var span in spans)
            {
                // base properties
                Assert.Equal(expectedSpanType, span.Type);

                // errors
                Assert.Equal(isError, span.Error == 1);
                if (span == aspnetSpan)
                {
                    Assert.Equal(expectedAspNetErrorType, span.Tags.GetValueOrDefault(Tags.ErrorType));
                    Assert.Equal(expectedAspNetErrorMessage, span.Tags.GetValueOrDefault(Tags.ErrorMsg));
                }
                else if (span == innerSpan)
                {
                    Assert.Equal(expectedErrorType, span.Tags.GetValueOrDefault(Tags.ErrorType));
                    Assert.Equal(expectedErrorMessage, span.Tags.GetValueOrDefault(Tags.ErrorMsg));
                }

                // other tags
                Assert.Equal(SpanKinds.Server, span.Tags.GetValueOrDefault(Tags.SpanKind));
                Assert.Equal(expectedServiceVersion, span.Tags.GetValueOrDefault(Tags.Version));
            }

            if (expectedTags?.Values is not null)
            {
                foreach (var expectedTag in expectedTags)
                {
                    Assert.Equal(expectedTag.Value, innerSpan.Tags.GetValueOrDefault(expectedTag.Key));
                }
            }
        }

        protected async Task AssertAspNetSpanOnly(
            string path,
            MockTracerAgent agent,
            int httpPort,
            HttpStatusCode expectedHttpStatusCode,
            bool isError,
            string expectedErrorType,
            string expectedErrorMessage,
            string expectedSpanType,
            string expectedResourceName,
            string expectedServiceVersion)
        {
            IImmutableList<MockSpan> spans;

            using (var httpClient = new HttpClient())
            {
                // disable tracing for this HttpClient request
                httpClient.DefaultRequestHeaders.Add(HttpHeaderNames.TracingEnabled, "false");
                var testStart = DateTime.UtcNow;
                var response = await httpClient.GetAsync($"http://localhost:{httpPort}" + path);
                var content = await response.Content.ReadAsStringAsync();
                Output.WriteLine($"[http] {response.StatusCode} {content}");
                Assert.Equal(expectedHttpStatusCode, response.StatusCode);

                spans = agent.WaitForSpans(
                    count: 1,
                    minDateTime: testStart,
                    operationName: "aspnet.request",
                    returnAllOperations: true);

                Assert.True(spans.Count == 1, $"expected two span, saw {spans.Count}");
            }

            var span = spans[0];

            // base properties
            Assert.Equal(expectedResourceName, span.Resource);
            Assert.Equal(expectedSpanType, span.Type);

            // errors
            Assert.Equal(isError, span.Error == 1);
            Assert.Equal(expectedErrorType, span.Tags.GetValueOrDefault(Tags.ErrorType));
            Assert.Equal(expectedErrorMessage, span.Tags.GetValueOrDefault(Tags.ErrorMsg));

            // other tags
            Assert.Equal(SpanKinds.Server, span.Tags.GetValueOrDefault(Tags.SpanKind));
            Assert.Equal(expectedServiceVersion, span.Tags.GetValueOrDefault(Tags.Version));
        }

        private bool IsServerSpan(MockSpan span) =>
            span.Tags.GetValueOrDefault(Tags.SpanKind) == SpanKinds.Server;

        private async Task<string> DownloadProcdumpZipAndExtract()
        {
            // We don't know if procdump is available, so download it fresh
            const string url = @"https://download.sysinternals.com/files/Procdump.zip";
            var client = new HttpClient();
            var zipFilePath = Path.GetTempFileName();
            Output.WriteLine($"Downloading Procdump to '{zipFilePath}'");
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                using var bodyStream = await response.Content.ReadAsStreamAsync();
                using Stream streamToWriteTo = File.Open(zipFilePath, FileMode.Create);
                await bodyStream.CopyToAsync(streamToWriteTo);
            }

            var unpackedDirectory = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(Path.GetTempFileName()));
            Output.WriteLine($"Procdump downloaded. Unpacking to '{unpackedDirectory}'");
            System.IO.Compression.ZipFile.ExtractToDirectory(zipFilePath, unpackedDirectory);

            var procDump = Path.Combine(unpackedDirectory, "procdump.exe");
            return procDump;
        }

        private bool CaptureMemoryDump(string tool, string args)
        {
            Output.WriteLine($"Capturing memory dump using '{tool} {args}'");

            using var dumpToolProcess = Process.Start(new ProcessStartInfo(tool, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            using var helper = new ProcessHelper(dumpToolProcess);
            dumpToolProcess.WaitForExit(30_000);
            helper.Drain();
            Output.WriteLine($"[dump][stdout] {helper.StandardOutput}");
            Output.WriteLine($"[dump][stderr] {helper.ErrorOutput}");

            if (dumpToolProcess.ExitCode == 0)
            {
                Output.WriteLine($"Memory dump successfully captured using '{tool} {args}'.");
            }
            else
            {
                Output.WriteLine($"Failed to capture memory dump using '{tool} {args}'. {tool}'s exit code was {dumpToolProcess.ExitCode}.");
            }

            return true;
        }

        protected internal class TupleList<T1, T2> : List<Tuple<T1, T2>>
        {
            public void Add(T1 item, T2 item2)
            {
                Add(new Tuple<T1, T2>(item, item2));
            }
        }
    }
}
