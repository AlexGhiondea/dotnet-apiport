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
            DisplayName = "DGML",
            MimeType = "application/xml",
            FileExtension = ".dgml"
        };

        public void WriteStream(Stream stream, AnalyzeResponse response, AnalyzeRequest request)
        {
            XDocument file = XDocument.Parse(_template);
            XElement root = file.Root;
            root.SetAttributeValue("Title", request.ApplicationName);

            XElement nodes = root.Element(_nameSpace + "Nodes");
            XElement links = root.Element(_nameSpace + "Links");

            ReportingResult analysisResult = response.ReportingResult;

            if (analysisResult.GetAssemblyUsageInfo().Any())
            {
                var targets = analysisResult.Targets;
                for (int i = 0; i < targets.Count; i++)
                {
                    string target = targets[i].FullName;
                    Guid nodeGuid = Guid.NewGuid();
                    _nodesDictionary.Add(target, nodeGuid);
                    nodes.Add(new XElement(_nameSpace + "Node",
                        new XAttribute("Id", nodeGuid),
                        new XAttribute("Label", target),
                        new XAttribute("Category", "Target"),
                        new XAttribute("Group", "Collapsed")));
                }

                List<AssemblyUsageInfo> assemblyUsageInfo = analysisResult.GetAssemblyUsageInfo().OrderBy(a => a.SourceAssembly.AssemblyIdentity).ToList();
                IDictionary<string, ICollection<string>> unresolvedAssemblies = analysisResult.GetUnresolvedAssemblies();
                foreach (var item in assemblyUsageInfo)
                {
                    string assemblyName = analysisResult.GetNameForAssemblyInfo(item.SourceAssembly);
                    for (int i = 0; i < item.UsageData.Count; i++)
                    {
                        TargetUsageInfo usageInfo = item.UsageData[i];
                        var portabilityIndex = Math.Round(usageInfo.PortabilityIndex * 100.0, 2);
                        string framework = targets[i].FullName;
                        Guid nodeGuid = GetOrCreateGuid($"{item.SourceAssembly.GetFullAssemblyIdentity()},TFM:{framework}");

                        nodes.Add(new XElement(_nameSpace + "Node",
                            new XAttribute("Id", nodeGuid),
                            new XAttribute("Label", $"{assemblyName} {portabilityIndex}%"),
                            new XAttribute("Category", GetCategory(portabilityIndex)),
                            new XAttribute("PortabilityIndex", $"{portabilityIndex}%")));

                        if (_nodesDictionary.TryGetValue(framework, out Guid frameworkGuid))
                        {
                            links.Add(new XElement(_nameSpace + "Link",
                                new XAttribute("Source", frameworkGuid),
                                new XAttribute("Target", nodeGuid),
                                new XAttribute("Category", "Contains")));
                        }
                    }

                    IList<AssemblyReferenceInformation> references = item.SourceAssembly.AssemblyReferences;
                    for (int i = 0; i < targets.Count; i++)
                    {
                        string framework = targets[i].FullName;
                        for (int j = 0; j < references.Count; j++)
                        {
                            var reference = references[j].ToString();
                            if (unresolvedAssemblies.TryGetValue(reference, out var _))
                            {
                                _nodesDictionary.Add(reference, Guid.NewGuid());
                            }
                        }
                    }
                }
            }

            using (var ms = new MemoryStream())
            {
                file.Save(ms);
                ms.Position = 0;
                ms.CopyTo(stream);
            }
        }

        private Guid GetOrCreateGuid(string nodeLabel)
        {
            if (!_nodesDictionary.TryGetValue(nodeLabel, out Guid guid))
            {
                guid = Guid.NewGuid();
                _nodesDictionary.Add(nodeLabel, guid);
            }

            return guid;
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

        private readonly List<Tuple<Guid, Guid>> _references = new List<Tuple<Guid, Guid>>();

        private readonly Dictionary<string, Guid> _nodesDictionary = new Dictionary<string, Guid>();

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
                <Category Id=""Target"" Background=""white"" />
                <Category Id=""Missing"" Background=""white"" />
            </Categories>
            <Properties>
                <Property Id=""PortabilityIndex"" Label=""Portability Index"" DataType=""System.String"" />
            </Properties>
            </DirectedGraph>";

    }
}
