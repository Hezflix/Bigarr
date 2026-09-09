// Bigarr addition (not vendored) — replaces NLog in the vendored Radarr parser. GPL-3.0.
// The upstream files declare `private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(X));`
// and then call Logger.Trace/Debug/Warn/Error. This shim keeps those call sites byte-identical
// while removing the NLog dependency. It is a no-op unless Bigarr sets ParserLog.Sink.
// See VENDORED.md.

using System;

namespace Bigarr.Parsing.Internal
{
    /// <summary>
    /// Minimal NLog-shaped logger used by the vendored parser. Silent by default.
    /// Set <see cref="Sink"/> once at startup to forward parser diagnostics somewhere.
    /// </summary>
    public sealed class ParserLog
    {
        /// <summary>
        /// Optional global sink: (level, logger name, message, exception). Silent when null.
        /// </summary>
        public static Action<ParserLogLevel, string, string, Exception> Sink { get; set; }

        private readonly string _name;

        private ParserLog(string name)
        {
            _name = name;
        }

        internal static ParserLog For(Type type) => new ParserLog(type?.FullName ?? "Bigarr.Parsing");

        public void Trace(string message) => Write(ParserLogLevel.Trace, message, null);

        public void Trace(object message) => Write(ParserLogLevel.Trace, message?.ToString(), null);

        public void Trace(string message, params object[] args) => Write(ParserLogLevel.Trace, Format(message, args), null);

        public void Debug(string message) => Write(ParserLogLevel.Debug, message, null);

        public void Debug(object message) => Write(ParserLogLevel.Debug, message?.ToString(), null);

        public void Debug(string message, params object[] args) => Write(ParserLogLevel.Debug, Format(message, args), null);

        public void Debug(Exception exception, string message, params object[] args) => Write(ParserLogLevel.Debug, Format(message, args), exception);

        public void Warn(string message) => Write(ParserLogLevel.Warn, message, null);

        public void Warn(string message, params object[] args) => Write(ParserLogLevel.Warn, Format(message, args), null);

        public void Error(string message) => Write(ParserLogLevel.Error, message, null);

        public void Error(string message, params object[] args) => Write(ParserLogLevel.Error, Format(message, args), null);

        public void Error(Exception exception, string message, params object[] args) => Write(ParserLogLevel.Error, Format(message, args), exception);

        private void Write(ParserLogLevel level, string message, Exception exception)
        {
            Sink?.Invoke(level, _name, message, exception);
        }

        private static string Format(string message, object[] args)
        {
            if (message == null || args == null || args.Length == 0)
            {
                return message;
            }

            try
            {
                return string.Format(message, args);
            }
            catch (FormatException)
            {
                // Never let a diagnostic message break parsing.
                return message;
            }
        }
    }

    public enum ParserLogLevel
    {
        Trace,
        Debug,
        Warn,
        Error
    }
}
