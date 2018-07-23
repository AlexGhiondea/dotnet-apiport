using Microsoft.Fx.Portability.ObjectModel;
using Microsoft.Fx.Portability.Reporting;
using System;
using System.IO;

namespace Microsoft.Fx.Portability.Reports.DGML
{
    public class DGMLOutputWriter : IReportWriter
    {
        public ResultFormatInformation Format => throw new NotImplementedException();

        public void WriteStream(Stream stream, AnalyzeResponse response)
        {
            throw new NotImplementedException();
        }
    }
}
