// <copyright file="LogEntryWatcher.cs" company="Datadog">
// Unless explicitly stated otherwise all files in this repository are licensed under the Apache 2 License.
// This product includes software developed at Datadog (https://www.datadoghq.com/). Copyright 2017 Datadog, Inc.
// </copyright>

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Datadog.Trace.Logging;

namespace Datadog.Trace.TestHelpers;

public class LogEntryWatcher : IDisposable
{
    private readonly FileSystemWatcher _fileWatcher;
    private StreamReader _reader;

    public LogEntryWatcher(string logFilePattern)
    {
        var logPath = DatadogLogging.GetLogDirectory();
        _fileWatcher = new FileSystemWatcher { Path = logPath, Filter = logFilePattern, EnableRaisingEvents = true };

        var dir = new DirectoryInfo(logPath);
        var lastFile = dir
                      .GetFiles(logFilePattern)
                      .OrderBy(info => info.LastWriteTime)
                      .LastOrDefault();

        if (lastFile != null && lastFile.LastWriteTime.Date == DateTime.Today)
        {
            SetStream(lastFile.FullName);
            _reader.ReadToEnd();
        }

        _fileWatcher.Created += NewLogFileCreated;
    }

    public void Dispose()
    {
        _fileWatcher?.Dispose();
        _reader?.Dispose();
    }

    public Task WaitForLogEntry(string logEntry, TimeSpan? timeout = null) => WaitForLogEntries(new[] { logEntry }, timeout);

    public async Task WaitForLogEntries(string[] logEntries, TimeSpan? timeout = null)
    {
        using var cancellationSource = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));

        var i = 0;
        while (logEntries.Length > i && !cancellationSource.IsCancellationRequested)
        {
            if (_reader == null)
            {
                await Task.Delay(100);
                continue;
            }

            var line = await _reader.ReadLineAsync();
            if (line != null)
            {
                if (line.Contains(logEntries[i]))
                {
                    i++;
                }
            }
            else
            {
                await Task.Delay(100);
            }
        }

        if (i != logEntries.Length)
        {
            throw new InvalidOperationException(_reader == null ? $"Log file was not found for path: {_fileWatcher.Path} with file pattern {_fileWatcher.Filter}" : "Log entry was not found.");
        }
    }

    private void SetStream(string filePath)
    {
        var reader = _reader;

        try
        {
            var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            _reader = new StreamReader(fileStream, Encoding.UTF8);
        }
        finally
        {
            reader?.Dispose();
        }
    }

    private void NewLogFileCreated(object sender, FileSystemEventArgs e)
    {
        SetStream(e.FullPath);
    }
}
