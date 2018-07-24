// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Fx.Portability.ObjectModel;
using Microsoft.Fx.Portability.Reporting;
using Microsoft.Fx.Portability.Reporting.ObjectModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Microsoft.Fx.Portability.Reports.DGML
{
    public class DGMLOutputWriter : IReportWriter
    {
        public ResultFormatInformation Format => new ResultFormatInformation()
        {
            DisplayName="DGML",
            MimeType="application/xml",
            FileExtension=".dgml"
        };

        public void WriteStream(Stream stream, AnalyzeResponse response, AnalyzeRequest request)
        {
            XDocument file = XDocument.Parse(_template);
            XElement root = file.Root;
            root.SetAttributeValue("Title", request.ApplicationName);

            XElement nodes = root.Element(_nameSpace + "Nodes");

            ReportingResult analysisResult = response.ReportingResult;

            if (analysisResult.GetAssemblyUsageInfo().Any())
            {
                _assemblyNodes = new Dictionary<string, Guid>();

                foreach (var item in analysisResult.GetAssemblyUsageInfo().OrderBy(a => a.SourceAssembly.AssemblyIdentity))
                {
                    string assemblyName = analysisResult.GetNameForAssemblyInfo(item.SourceAssembly);
                    Guid nodeGuid = GetOrCreateGuid(item.SourceAssembly.GetFullAssemblyIdentity());
                    var portabilityIndex = item.UsageData.Select(pui => (object)(Math.Round(pui.PortabilityIndex * 100.0, 2))).FirstOrDefault();
                    nodes.Add(new XElement(_nameSpace + "Node",
                        new XAttribute("Id", nodeGuid),
                        new XAttribute("Label", $"{assemblyName} {portabilityIndex}%"),
                        new XAttribute("Category", GetCategory((double)portabilityIndex)),
                        new XAttribute("PortabilityIndex", $"{portabilityIndex}%")));
                }
            }

            using (var ms = new MemoryStream())
            {
                file.Save(ms);
                ms.Position = 0;
                ms.CopyTo(stream);
            }
        }

        private Guid GetOrCreateGuid(string assembly)
        {
            if (!_assemblyNodes.TryGetValue(assembly, out Guid result))
            {
                result = Guid.NewGuid();
                _assemblyNodes.Add(assembly, result);
            }

            return result;
        }

        private static string GetCategory(double probabilityIndex)
        {
            if (probabilityIndex >= 90.0)
                return "VeryHigh";
            if (probabilityIndex >= 75.0)
                return "High";
            if (probabilityIndex >= 50.0)
                return "Medium";
            if (probabilityIndex >= 30.0)
                return "MediumLow";

            return "Low";
        }

        private Dictionary<string, Guid> _assemblyNodes;

        private readonly XNamespace _nameSpace = "http://schemas.microsoft.com/vs/2009/dgml";

        private readonly string _template =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
            <DirectedGraph xmlns=""http://schemas.microsoft.com/vs/2009/dgml"">
            <Nodes>
            </Nodes>
            <Links>
            </Links>
            <Categories>
                <Category Id=""VeryHigh"" Background=""#009933"" />
                <Category Id=""High"" Background=""#ffff66"" />
                <Category Id=""Medium"" Background=""#ff9900"" />
                <Category Id=""MediumLow"" Background=""#ff3300"" />
                <Category Id=""Low"" Background=""#990000"" />
            </Categories>
            <Properties>
                <Property Id=""PortabilityIndex"" Label=""Portability Index"" DataType=""System.Double"" />
            </Properties>
            </DirectedGraph>";

    }
}
