﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Microsoft.Fx.Portability.Reports.DGML
{
    /// <summary>
    /// This class will manage the IDs that we generate for the DGML graph
    /// </summary>
    class DGMLManager
    {
        private readonly Dictionary<string, Guid> _nodesDictionary = new Dictionary<string, Guid>();

        private XElement nodes;

        private XElement links;

        private readonly XNamespace _nameSpace = "http://schemas.microsoft.com/vs/2009/dgml";
        #region DGML template
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
        #endregion


        XDocument file;
        public DGMLManager()
        {
            file = XDocument.Parse(_template);
            XElement root = file.Root;

            nodes = root.Element(_nameSpace + "Nodes");
            links = root.Element(_nameSpace + "Links");
        }

        public void SetTitle(string title)
        {
            file.Root.SetAttributeValue("Title", title);
        }

        public bool TryGetId(string value, out Guid frameworkGuid)
        {
            return _nodesDictionary.TryGetValue(value, out frameworkGuid);
        }

        internal void AddId(string value, Guid nodeGuid)
        {
            _nodesDictionary.Add(value, nodeGuid);
        }

        internal void AddLink(Guid source, Guid target, string category = null)
        {
            var element = new XElement(_nameSpace + "Link",
                new XAttribute("Source", source),
                new XAttribute("Target", target));

            if (category != null)
                element.SetAttributeValue("Category", category);

            links.Add(element);
        }

        internal void AddNode(Guid id, string label, string category, string portabilityIndex = null, string group = null)
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

        internal void Save(Stream stream)
        {
            using (var ms = new MemoryStream())
            {
                file.Save(ms);
                ms.Position = 0;
                ms.CopyTo(stream);
            };
        }

        internal bool GetOrCreateGuid(string nodeLabel, out Guid guid)
        {
            if (!_nodesDictionary.TryGetValue(nodeLabel, out guid))
            {
                guid = Guid.NewGuid();
                _nodesDictionary.Add(nodeLabel, guid);
                return false;
            }

            return true;
        }
    }
}
