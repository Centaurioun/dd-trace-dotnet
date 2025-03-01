// dllmain.cpp : Defines the entry point for the DLL application.
#include "cor_profiler_class_factory.h"

#include "log.h"
#include "dynamic_dispatcher.h"
#include "util.h"

using namespace datadog::shared::nativeloader;

IDynamicDispatcher* dispatcher;

extern "C"
{
    BOOL STDMETHODCALLTYPE DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
    {
        // Perform actions based on the reason for calling.
        switch (ul_reason_for_call)
        {
            case DLL_PROCESS_ATTACH:
            {
                // Initialize once for each new process.
                // Return FALSE to fail DLL load.
                
                constexpr const bool IsLogDebugEnabledDefault = false;
                bool isLogDebugEnabled;

                shared::WSTRING isLogDebugEnabledStr = shared::GetEnvironmentValue(EnvironmentVariables::DebugLogEnabled);

                // no environment variable set
                if (isLogDebugEnabledStr.empty())
                {
                    Log::Info("No \"", EnvironmentVariables::DebugLogEnabled, "\" environment variable has been found.",
                              " Enable debug log = ", IsLogDebugEnabledDefault, " (default).");

                    isLogDebugEnabled = IsLogDebugEnabledDefault;
                }
                else
                {
                    if (!shared::TryParseBooleanEnvironmentValue(isLogDebugEnabledStr, isLogDebugEnabled))
                    {
                        // invalid value for environment variable
                        Log::Info("Non boolean value \"", isLogDebugEnabledStr, "\" for \"",
                                  EnvironmentVariables::DebugLogEnabled, "\" environment variable.",
                                  " Enable debug log = ", IsLogDebugEnabledDefault, " (default).");

                        isLogDebugEnabled = IsLogDebugEnabledDefault;
                    }
                    else
                    {
                        // take environment variable into account
                        Log::Info("Enable debug log = ", isLogDebugEnabled, " from (", EnvironmentVariables::DebugLogEnabled, " environment variable)");
                    }
                }

                if (isLogDebugEnabled)
                {
                    Log::EnableDebug();
                }

                Log::Debug("DllMain: DLL_PROCESS_ATTACH");
                Log::Debug("DllMain: Pointer size: ", 8 * sizeof(void*), " bits.");

                dispatcher = new DynamicDispatcherImpl();
                dispatcher->LoadConfiguration(GetConfigurationFilePath());

                // *****************************************************************************************************************
                break;
            }

            case DLL_PROCESS_DETACH:
                // Perform any necessary cleanup.
                Log::Debug("DllMain: DLL_PROCESS_DETACH");

                break;
        }
        return TRUE; // Successful DLL_PROCESS_ATTACH.
    }

    HRESULT STDMETHODCALLTYPE DllGetClassObject(REFCLSID rclsid, REFIID riid, LPVOID* ppv)
    {
        Log::Debug("DllGetClassObject");

        // {846F5F1C-F9AE-4B07-969E-05C26BC060D8}
        const GUID CLSID_CorProfiler = {0x846f5f1c, 0xf9ae, 0x4b07, {0x96, 0x9e, 0x5, 0xc2, 0x6b, 0xc0, 0x60, 0xd8}};

        if (ppv == NULL || rclsid != CLSID_CorProfiler)
        {
            return E_FAIL;
        }

        auto factory = new CorProfilerClassFactory(dispatcher);
        if (factory == NULL)
        {
            return E_FAIL;
        }

        return factory->QueryInterface(riid, ppv);
    }

    HRESULT STDMETHODCALLTYPE DllCanUnloadNow()
    {
        Log::Debug("DllCanUnloadNow");

        return dispatcher->DllCanUnloadNow();
    }
}