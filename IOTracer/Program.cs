// Program.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using IOTracer.handlers;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace IOTracer
{
    class Program
    {

        static void Main(string[] args)
        {
            Tracer trc = new Tracer();
            trc.Trace();
        }

    }
}
