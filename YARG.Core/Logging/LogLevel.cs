﻿namespace YARG.Core.Logging
{
    public enum LogLevel
    {
        Trace,
        Debug,
        Info,
        Warning,
        Error,
        Exception,
    }

    public static class LogLevelExtensions
    {
        public static string AsLevelString(this LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace     => "Trace",
                LogLevel.Debug     => "Debug",
                LogLevel.Info      => "Info",
                LogLevel.Warning   => "Warning",
                LogLevel.Error     => "Error",
                LogLevel.Exception => "Exception",
                _                  => "UNKNOWN",
            };
        }
    }
}