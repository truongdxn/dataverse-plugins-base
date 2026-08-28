using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;

namespace Dataverse.Plugins.Testing
{
    /// <summary>
    /// Collects trace output instead of discarding it, so a test can assert on what a plugin said.
    /// <para>
    /// Tracing is the one thing a plugin does that must never throw - in production a bad format
    /// string would turn a working plugin into a failing one. This mirrors that: a format that
    /// cannot be applied is recorded raw rather than raised.
    /// </para>
    /// </summary>
    public class FakeTracingService : ITracingService
    {
        private readonly List<string> _lines = new List<string>();

        /// <summary>Everything traced so far, in order.</summary>
        public IReadOnlyList<string> Lines
        {
            get { return _lines; }
        }

        /// <summary>All trace output as one string, which is what a failing assert should print.</summary>
        public string Text
        {
            get { return string.Join(Environment.NewLine, _lines.ToArray()); }
        }

        /// <summary>True when any traced line contains <paramref name="fragment"/>.</summary>
        public bool Contains(string fragment)
        {
            return _lines.Exists(line => line != null && line.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public void Trace(string format, params object[] args)
        {
            if (format == null)
            {
                return;
            }

            if (args == null || args.Length == 0)
            {
                _lines.Add(format);
                return;
            }

            try
            {
                _lines.Add(string.Format(CultureInfo.InvariantCulture, format, args));
            }
            catch (FormatException)
            {
                _lines.Add(format);
            }
        }
    }
}
