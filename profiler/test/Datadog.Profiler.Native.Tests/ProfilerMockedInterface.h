#pragma once
#include "gmock/gmock.h"
#include "gtest/gtest.h"

#include "shared/src/native-src/dd_filesystem.hpp"
// namespace fs is an alias defined in "dd_filesystem.hpp"

#include "Configuration.h"
#include "IApplicationStore.h"
#include "IExporter.h"
#include "IMetricsSender.h"
#include "IRuntimeIdStore.h"
#include "ISamplesCollector.h"
#include "ISamplesProvider.h"
#include "Sample.h"
#include "TagsHelper.h"

class MockConfiguration : public IConfiguration
{
public:
    ~MockConfiguration() override = default;
    MOCK_METHOD(bool, IsDebugLogEnabled, (), (const override));
    MOCK_METHOD(fs::path const&, GetLogDirectory, (), (const override));
    MOCK_METHOD(fs::path const&, GetProfilesOutputDirectory, (), (const override));
    MOCK_METHOD(bool, IsNativeFramesEnabled, (), (const override));
    MOCK_METHOD(bool, IsOperationalMetricsEnabled, (), (const override));
    MOCK_METHOD(std::chrono::seconds, GetUploadInterval, (), (const override));
    MOCK_METHOD(tags const&, GetUserTags, (), (const override));
    MOCK_METHOD(std::string const&, GetVersion, (), (const override));     // return DD_VERSION, Unspecified-Version if not specified
    MOCK_METHOD(std::string const&, GetEnvironment, (), (const override)); // return DD_ENV, Unspecified-Env if not specified
    MOCK_METHOD(std::string const&, GetHostname, (), (const override));    // return the machine hostname
    MOCK_METHOD(std::string const&, GetAgentUrl, (), (const override));
    MOCK_METHOD(std::string const&, GetAgentHost, (), (const override));
    MOCK_METHOD(int, GetAgentPort, (), (const override));
    MOCK_METHOD(std::string const&, GetSite, (), (const override));
    MOCK_METHOD(std::string const&, GetApiKey, (), (const override));
    MOCK_METHOD(std::string const&, GetServiceName, (), (const override));
    MOCK_METHOD(bool, IsFFLibddprofEnabled, (), (const override));
    MOCK_METHOD(bool, IsAgentless, (), (const override));
    MOCK_METHOD(bool, IsWallTimeProfilingEnabled, (), (const override));
    MOCK_METHOD(bool, IsCpuProfilingEnabled, (), (const override));
    MOCK_METHOD(bool, IsExceptionProfilingEnabled, (), (const override));
    MOCK_METHOD(int, ExceptionSampleLimit, (), (const override));
    MOCK_METHOD(bool, IsAllocationProfilingEnabled, (), (const override));
    MOCK_METHOD(bool, IsContentionProfilingEnabled, (), (const override));
    MOCK_METHOD(double, MinimumCores, (), (const override));
};

class MockExporter : public IExporter
{
public:
    MOCK_METHOD(void, Add, (Sample const& sample), (override));
    MOCK_METHOD(bool, Export, (), (override));
    MOCK_METHOD(void, SetEndpoint, (const std::string& runtimeId, uint64_t traceId, const std::string& endpoint), (override));
};

class MockSamplesCollector : public ISamplesCollector
{
public:
    MOCK_METHOD(void, Register, (ISamplesProvider * sampleProvider), (override));
};

class MockSampleProvider : public ISamplesProvider
{
public:
    MOCK_METHOD(std::list<Sample>, GetSamples, (), (override));
};

class MockMetricsSender : public IMetricsSender
{
public:
    ~MockMetricsSender() override = default;

    bool Gauge(std::string const& name, double value) override
    {
        ++_nbCallsToGauge;
        return true;
    }
    bool Counter(const std::string& name, std::uint64_t value, const Tags& additionalTags = {}) override
    {
        ++_nbCallsToCounter;
        return true;
    }

    bool WasCounterCalled() const
    {
        return _nbCallsToCounter > 0;
    }

    bool WasGaugerCalled() const
    {
        return _nbCallsToGauge > 0;
    }

private:
    std::uint32_t _nbCallsToCounter;
    std::uint32_t _nbCallsToGauge;
};

class MockApplicationStore : public IApplicationStore
{
public:
    MOCK_METHOD(ApplicationInfo, GetApplicationInfo, (const std::string& runtimeId), (override));
    MOCK_METHOD(void, SetApplicationInfo, (const std::string&, const std::string&, const std::string&, const std::string&), (override));
    MOCK_METHOD(const char*, GetName, (), (override));
    MOCK_METHOD(bool, Start, (), (override));
    MOCK_METHOD(bool, Stop, (), (override));
};

class MockRuntimeIdStore : public IRuntimeIdStore
{
public:
    MOCK_METHOD(const char*, GetId, (AppDomainID appDomainId), (override));
};

template <typename T, typename U, typename... Args>
std::pair<std::unique_ptr<T>, U&> CreateMockForUniquePtr(Args... args)
{
    std::unique_ptr<T> interf = std::make_unique<U>(args...);
    auto mock = static_cast<U*>(interf.get());
    return {std::move(interf), *mock};
}

std::tuple<std::unique_ptr<IConfiguration>, MockConfiguration&> CreateConfiguration();

std::tuple<std::shared_ptr<ISamplesProvider>, MockSampleProvider&> CreateSamplesProvider();

std::tuple<std::unique_ptr<IExporter>, MockExporter&> CreateExporter();
std::tuple<std::unique_ptr<ISamplesCollector>, MockSamplesCollector&> CreateSamplesCollector();

Sample CreateSample(std::string_view runtimeId, const std::vector<std::pair<std::string, std::string>>& callstack, const std::vector<std::pair<std::string, std::string>>& labels, std::int64_t value);

std::vector<std::pair<std::string, std::string>> CreateCallstack(int depth);