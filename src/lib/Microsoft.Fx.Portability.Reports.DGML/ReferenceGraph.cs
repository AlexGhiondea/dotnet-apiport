using Microsoft.Fx.Portability.ObjectModel;
using Microsoft.Fx.Portability.Reporting.ObjectModel;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Microsoft.Fx.Portability.Reports.DGML
{
    class ReferenceGraph
    {
        public Dictionary<ReferenceNode, ReferenceNode> Nodes { get; set; }

        public ReferenceGraph()
        {
            Nodes = new Dictionary<ReferenceNode, ReferenceNode>(new ReferenceNodeComparer());
        }

        public ReferenceNode GetOrAddNodeForAssembly(ReferenceNode node)
        {
            if (Nodes.ContainsKey(node))
                return Nodes[node];

            Nodes.Add(node, node);
            return node;
        }
    }

    class ReferenceNodeComparer : IEqualityComparer<ReferenceNode>
    {
        public bool Equals(ReferenceNode x, ReferenceNode y)
        {
            return x.Assembly == y.Assembly;
        }

        public int GetHashCode(ReferenceNode obj)
        {
            return obj.Assembly.GetHashCode();
        }
    }

    class ReferenceNode
    {
        public string SimpleName
        {
            get
            {
                if (!IsMissing)
                    return new AssemblyName(Assembly).Name;

                return "Unresolved: " + new AssemblyName(Assembly).Name;
            }
        }
        public ReferenceNode(string AssemblyName, bool unresolved = false)
        {
            Assembly = AssemblyName;
            this.Unresolved = unresolved;
            Nodes = new HashSet<ReferenceNode>();
        }
        public override int GetHashCode()
        {
            return Assembly.GetHashCode();
        }

        public void AddReferenceToNode(ReferenceNode node)
        {
            Nodes.Add(node);
        }

        public override string ToString()
        {
            return Assembly;
        }

        public List<TargetUsageInfo> UsageData { get; set; }

        public string Assembly { get; set; }

        public bool Unresolved { get; set; }

        public HashSet<ReferenceNode> Nodes { get; set; }
        public bool IsMissing { get; internal set; }
    }
}
