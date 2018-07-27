using Microsoft.Fx.Portability.ObjectModel;
using Microsoft.Fx.Portability.Reporting.ObjectModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Microsoft.Fx.Portability.Reports.DGML
{
    class ReferenceGraph
    {
        public static ReferenceGraph CreateGraph(AnalyzeResponse response, AnalyzeRequest request)
        {
            ReferenceGraph rg = new ReferenceGraph();

            // get the list of assemblies that have some data reported for them.
            var assembliesWithData = response.ReportingResult.GetAssemblyUsageInfo().ToDictionary(x => x.SourceAssembly.AssemblyIdentity, x => x.UsageData);

            var unresolvedAssemblies = response.ReportingResult.GetUnresolvedAssemblies().Select(x => x.Key).ToList();

            // Add every user specified assembly to the graph
            foreach (var userAsem in request.UserAssemblies)
            {
                var node = rg.GetOrAddNodeForAssembly(new ReferenceNode(userAsem.AssemblyIdentity));

                //for this node, make sure we capture the data, if we have it.
                if (assembliesWithData.ContainsKey(node.Assembly))
                {
                    node.UsageData = assembliesWithData[node.Assembly];
                }

                // create nodes for all the references, if non platform.
                foreach (var reference in userAsem.AssemblyReferences)
                {
                    if (!(assembliesWithData.ContainsKey(reference.ToString()) || unresolvedAssemblies.Contains(reference.ToString())))
                    {
                        // platform reference (not in the user specified asssemblies and not an unresolved assembly.
                        continue;
                    }

                    var refNode = rg.GetOrAddNodeForAssembly(new ReferenceNode(reference.ToString()));

                    // if the reference is missing, flag it as such.
                    if (unresolvedAssemblies.Contains(reference.ToString()))
                    {
                        refNode.IsMissing = true;
                    }

                    node.AddReferenceToNode(refNode);
                }
            }

            if (rg.HasCycles())
            {
                // do nothing as we don't support this scenario.
                return rg;
            }

            rg.ComputeNewPortabilityIndex();

            return rg;
        }

        private void ComputeNewPortabilityIndex()
        {
            // TODO: update the index for the assemblies based on their references.
        }

        private bool HasCycles()
        {
            //TODO: implement
            return false;
        }

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
