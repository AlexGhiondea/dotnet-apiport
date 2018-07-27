// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Fx.Portability.Reporting.ObjectModel;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Microsoft.Fx.Portability.Reports.DGML
{
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

        public double GetPortabilityIndex(int target)
        {
            return UsageData[target].PortabilityIndex;
        }

        public double GetPortabilityIndexForReferences(int target)
        {
            if (ComputePortabilityIndexWithReferences)
            {
                // if we don't have any outgoing references, it is a good sign!
                if (Nodes.Count == 0)
                    return 1;

                // sum up the number of calls to available APIs and the ones for not available APIs for references.
                int availableApis = GetAvailableAPICalls(target);
                int unavailableApis = GetUnavailableAPICalls(target);

                // remove the calls from the current node.
                availableApis -= UsageData[target].GetAvailableAPICalls();
                unavailableApis -= UsageData[target].GetUnavailableAPICalls();

                // prevent Div/0
                if (availableApis == 0 && unavailableApis == 0)
                    return 0;

                return availableApis / ((double)availableApis + unavailableApis);
            }

            return 1; // if we can't compute them, assume the best
        }

        public int GetAvailableAPICalls(int target)
        {
            int availableApis = UsageData[target].GetAvailableAPICalls();
            foreach (var item in Nodes)
            {
                availableApis += item.GetAvailableAPICalls(target);
            }
            return availableApis;
        }

        public int GetUnavailableAPICalls(int target)
        {
            int unavailableApis = UsageData[target].GetUnavailableAPICalls();
            foreach (var item in Nodes)
            {
                unavailableApis += item.GetUnavailableAPICalls(target);
            }
            return unavailableApis;
        }

        public bool ComputePortabilityIndexWithReferences => true;

        public List<TargetUsageInfo> UsageData { get; set; }

        public string Assembly { get; set; }

        public bool Unresolved { get; set; }

        public HashSet<ReferenceNode> Nodes { get; set; }
        public bool IsMissing { get; internal set; }
    }
}
