// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Fx.Portability.ObjectModel;
using Microsoft.Fx.Portability.Reporting;
using Microsoft.Fx.Portability.Reporting.ObjectModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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

        XDocument file;

        public DGMLOutputWriter()
        {
            file = XDocument.Parse(_template);
            XElement root = file.Root;
            //TODO: root.SetAttributeValue("Title", request.ApplicationName);

            nodes = root.Element(_nameSpace + "Nodes");
            links = root.Element(_nameSpace + "Links");
        }

        public void WriteStream(Stream stream, AnalyzeResponse response, AnalyzeRequest request)
        {
            ReferenceGraph rg = ReferenceGraph.CreateGraph(response, request);

            ReportingResult analysisResult = response.ReportingResult;
            var targets = analysisResult.Targets;
            GenerateTargetContainers(targets);

            //for each target, let's generate the assemblies
            foreach (var node in rg.Nodes.Keys)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    double portabilityIndex = 0;
                    string missingTypes = null;
                    if (node.UsageData != null)
                    {
                        TargetUsageInfo usageInfo = node.UsageData[i];
                        portabilityIndex = Math.Round(usageInfo.PortabilityIndex * 100.0, 2);

                        missingTypes = GenerateMissingTypes(node.Assembly, analysisResult, i);
                    }

                    // generate the node
                    string tfm = targets[i].FullName;
                    GetOrCreateGuid($"{node.Assembly},TFM:{tfm}", out Guid nodeGuid);

                    AddNode(nodeGuid, $"{node.SimpleName}, {portabilityIndex}%", node.IsMissing ? "Unresolved" : GetCategory(portabilityIndex), portabilityIndex.ToString(), group: missingTypes.Length == 0 ? null : "Collapsed");

                    if (_nodesDictionary.TryGetValue(tfm, out Guid frameworkGuid))
                    {
                        AddLink(frameworkGuid, nodeGuid, "Contains");
                    }


                    if (!string.IsNullOrEmpty(missingTypes))
                    {
                        Guid commentGuid = Guid.NewGuid();
                        AddNode(commentGuid, missingTypes, "Comment");
                        AddLink(nodeGuid, commentGuid, "Contains");
                    }
                }
            }

            // generate the references.
            foreach (var node in rg.Nodes.Keys)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    // generate the node
                    string tfm = targets[i].FullName;
                    GetOrCreateGuid($"{node.Assembly},TFM:{tfm}", out Guid nodeGuid);

                    foreach (var refNode in node.Nodes)
                    {
                        GetOrCreateGuid($"{refNode.Assembly},TFM:{tfm}", out Guid refNodeGuid);

                        AddLink(nodeGuid, refNodeGuid);
                    }
                }
            }

            using (var ms = new MemoryStream())
            {
                file.Save(ms);
                ms.Position = 0;
                ms.CopyTo(stream);
            };

            return;
        }

        private string GenerateMissingTypes(string assembly, ReportingResult response, int i)
        {
            // for a given assembly identity and a given target usage, display the missing types
            //TODO: this is very allocation heavy.
            IEnumerable<MissingTypeInfo> missingTypesForAssembly = response.GetMissingTypes().Where(mt => mt.UsedIn.Any(x => x.AssemblyIdentity == assembly) && mt.IsMissing);
            var missingTypesForFramework = missingTypesForAssembly.Where(mt => mt.TargetStatus.ToList()[i] == "Not supported" || (mt.TargetVersionStatus.ToList()[i] > response.Targets[i].Version)).Select(x => x.DocId).OrderBy(x => x);

            return string.Join("\n", missingTypesForFramework);
        }

        private void GenerateTargetContainers(IList<System.Runtime.Versioning.FrameworkName> targets)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                string targetFramework = targets[i].FullName;
                Guid nodeGuid = Guid.NewGuid();
                _nodesDictionary.Add(targetFramework, nodeGuid);
                AddNode(nodeGuid, targetFramework, "Target", group: "Expanded");
            }
        }

        private bool GetOrCreateGuid(string nodeLabel, out Guid guid)
        {
            if (!_nodesDictionary.TryGetValue(nodeLabel, out guid))
            {
                guid = Guid.NewGuid();
                _nodesDictionary.Add(nodeLabel, guid);
                return false;
            }

            return true;
        }

        private static string GetCategory(double probabilityIndex)
        {
            if (probabilityIndex == 100.0)
                return "VeryHigh";
            if (probabilityIndex >= 75.0)
                return "High";
            if (probabilityIndex >= 50.0)
                return "Medium";
            if (probabilityIndex >= 30.0)
                return "MediumLow";

            return "Low";
        }

        private void AddLink(Guid source, Guid target, string category = null)
        {
            var element = new XElement(_nameSpace + "Link",
                new XAttribute("Source", source),
                new XAttribute("Target", target));

            if (category != null)
                element.SetAttributeValue("Category", category);

            links.Add(element);
        }

        private void AddNode(Guid id, string label, string category, string portabilityIndex = null, string group = null)
        {
            var element = new XElement(_nameSpace + "Node",
                new XAttribute("Id", id),
                new XAttribute("Label", label),
                new XAttribute("Category", category));

            if (portabilityIndex != null)
                element.SetAttributeValue("PortabilityIndex", portabilityIndex);
            if (group != null)
                element.SetAttributeValue("Group", group);

            nodes.Add(element);
        }

        private XElement nodes;

        private XElement links;

        private readonly Dictionary<string, Guid> _nodesDictionary = new Dictionary<string, Guid>();

        private readonly XNamespace _nameSpace = "http://schemas.microsoft.com/vs/2009/dgml";

        private readonly string _template =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
            <DirectedGraph xmlns=""http://schemas.microsoft.com/vs/2009/dgml"" Background=""grey"">
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
                <Category Id=""Unresolved"" Background=""red"" />
                <Category Id=""Comment"" Label=""Comment"" Description=""Represents a user defined comment on the diagram"" NavigationActionLabel=""Comments"" />
            </Categories>
            <Properties>
                <Property Id=""PortabilityIndex"" Label=""Portability Index"" DataType=""System.String"" />
            </Properties>
            <Styles>
                <Style TargetType=""Node"" GroupLabel=""Comment"" ValueLabel=""Has comment"">
                  <Condition Expression = ""HasCategory('Comment')"" />
                  <Setter Property=""Background"" Value=""#FFFFFACD"" />
                  <Setter Property=""Stroke"" Value=""#FFE5C365"" />
                  <Setter Property=""StrokeThickness"" Value=""1"" />
                  <Setter Property=""NodeRadius"" Value=""2"" />
                  <Setter Property=""MaxWidth"" Value=""250"" />
                </Style>
              </Styles>W
            </DirectedGraph>";

    }
}
