// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Fx.Portability.Analyzer.Resources;
using Microsoft.Fx.Portability.ObjectModel;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Cil;
using System.Reflection.Metadata.Cil.Decoder;
using System.Reflection.Metadata.Cil.Instructions;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Microsoft.Fx.Portability.Analyzer
{
    internal class ReflectionMetadataDependencyInfo : IDependencyInfo
    {
        private readonly IEnumerable<IAssemblyFile> _inputAssemblies;
        private readonly IDependencyFilter _assemblyFilter;

        private readonly ConcurrentDictionary<string, ICollection<string>> _unresolvedAssemblies = new ConcurrentDictionary<string, ICollection<string>>(StringComparer.Ordinal);
        private readonly ICollection<string> _assembliesWithError = new ConcurrentHashSet<string>(StringComparer.Ordinal);
        private readonly ICollection<AssemblyInfo> _userAssemblies = new ConcurrentHashSet<AssemblyInfo>();
        private readonly ConcurrentDictionary<MemberInfo, ICollection<AssemblyInfo>> _cachedDependencies = new ConcurrentDictionary<MemberInfo, ICollection<AssemblyInfo>>();

        private ReflectionMetadataDependencyInfo(IEnumerable<IAssemblyFile> inputAssemblies, IDependencyFilter assemblyFilter)
        {
            _inputAssemblies = inputAssemblies;
            _assemblyFilter = assemblyFilter;
        }

        public static ReflectionMetadataDependencyInfo ComputeDependencies(IEnumerable<IAssemblyFile> inputAssemblies, IDependencyFilter assemblyFilter, IProgressReporter progressReport)
        {
            var engine = new ReflectionMetadataDependencyInfo(inputAssemblies, assemblyFilter);

            engine.FindDependencies(progressReport);

            return engine;
        }

        public IDictionary<MemberInfo, ICollection<AssemblyInfo>> Dependencies
        {
            get { return _cachedDependencies; }
        }

        public IEnumerable<string> AssembliesWithErrors
        {
            get { return _assembliesWithError; }
        }

        public IDictionary<string, ICollection<string>> UnresolvedAssemblies
        {
            get { return _unresolvedAssemblies; }
        }

        public IEnumerable<AssemblyInfo> UserAssemblies
        {
            get { return _userAssemblies; }
        }

        public IDictionary<string, ICollection<string>> CallMap
        {
            get
            {
                return _callMap;
            }
        }

        private Dictionary<string, ICollection<string>> _callMap = new Dictionary<string, ICollection<string>>();

        private void FindDependencies(IProgressReporter progressReport)
        {
            //            _inputAssemblies.AsParallel().ForAll(file =>

            foreach (var file in _inputAssemblies)
            {
                //try
                //{
                foreach (var dependencies in GetDependencies(file))
                {
                    var m = new MemberInfo
                    {
                        MemberDocId = dependencies.MemberDocId,
                        TypeDocId = dependencies.TypeDocId,
                        DefinedInAssemblyIdentity = dependencies.DefinedInAssemblyIdentity?.ToString()
                    };

                    if (m.DefinedInAssemblyIdentity == null && !dependencies.IsPrimitive)
                    {
                        throw new InvalidOperationException("All non-primitive types should be defined in an assembly");
                    }

                    // Add this memberinfo
                    var newassembly = new HashSet<AssemblyInfo> { dependencies.CallingAssembly };

                    var assemblies = _cachedDependencies.AddOrUpdate(m, newassembly, (key, existingSet) =>
                    {
                        lock (existingSet)
                        {
                            existingSet.Add(dependencies.CallingAssembly);
                        }
                        return existingSet;
                    });
                }
                //}
                //catch (InvalidPEAssemblyException)
                //{
                //    // This often indicates a non-PE file
                //    _assembliesWithError.Add(file.Name);
                //}
                //catch (BadImageFormatException)
                //{
                //    // This often indicates a PE file with invalid contents (either because the assembly is protected or corrupted)
                //    _assembliesWithError.Add(file.Name);
                //}
            }

            //);

            // Clear out unresolved dependencies that were resolved during processing
            ICollection<string> collection;
            foreach (var assembly in _userAssemblies)
            {
                _unresolvedAssemblies.TryRemove(assembly.AssemblyIdentity, out collection);
            }
        }

        private IEnumerable<MemberDependency> GetDependencies(IAssemblyFile file)
        {
            //try
            //{
            using (var stream = file.OpenRead())
            using (var peFile = new PEReader(stream))
            {
                var metadataReader = GetMetadataReader(peFile);

                AddReferencedAssemblies(metadataReader);

                CilAssembly c = CilAssembly.Create(file.Name);

                // let's create a map of all the types that are used inside the code, and where they are comming from

                //Dictionary<string, HashSet<string>> mapTypeToCaller = new Dictionary<string, HashSet<string>>();

                HashSet<string> typeRefs = new HashSet<string>(c.TypeReferences.Select(n => n.FullName));

                foreach (var item in c.TypeReferences)
                {
                    Console.WriteLine($"{item.Token}: {item.FullName}");
                }

                foreach (var typeDef in c.TypeDefinitions)
                {
                    Console.WriteLine($"Found type {typeDef.Name}");
                    foreach (var memberDef in typeDef.MethodDefinitions)
                    {
                        Console.WriteLine($"  Found method {memberDef.Name}");
                        foreach (var item in memberDef.Instructions)
                        {
                            var val = item as CilStringInstructionWithParentType;
                            if (val == null)
                            {
                                continue;
                            }

                            string typeRef = string.Empty;
                            if (val.ParentType.Type == TypeType.Spec)
                            {
                                // do we have a type ref that matches?
                                typeRef = MatchTypeSpecToTypeRef(val.ParentType.Name, typeRefs);
                            }
                            else if (val.ParentType.Type == TypeType.Ref)
                            {
                                typeRef = val.ParentType.ToString();
                            }

                            if (!string.IsNullOrEmpty(typeRef))
                            {
                                typeRef = GetDocIdForType(typeRef);

                                ICollection<string> calledFrom;
                                if (!_callMap.TryGetValue(typeRef, out calledFrom))
                                {
                                    calledFrom = new HashSet<string>();
                                    _callMap[typeRef] = calledFrom;
                                }

                                calledFrom.Add(memberDef.MethodNameAndParameters());
                            }
                            Console.WriteLine(val.opCode + " " + val.Value);
                        }
                    }
                }

                var helper = new DependencyFinderEngineHelper(_assemblyFilter, metadataReader, file);
                helper.ComputeData();

                // Remember this assembly as a user assembly.
                _userAssemblies.Add(helper.CallingAssembly);

                return helper.MemberDependency;
            }
            //}
            //catch (Exception exc)
            //{
            //    // InvalidPEAssemblyExceptions may be expected and indicative of a non-PE file
            //    if (exc is InvalidPEAssemblyException) throw;

            //    // Other exceptions are unexpected, though, and wil benefit from
            //    // more details on the scenario that hit them
            //    throw new PortabilityAnalyzerException(string.Format(LocalizedStrings.MetadataParsingExceptionMessage, file.Name), exc);
            //}
        }


        private static string GetDocIdForType(string typeName)
        {
            // the name contains the assembly in [ ] 
            int pos = typeName.IndexOf(']');
            if (pos >= 0)
            {
                typeName = typeName.Substring(pos + 1);
            }
            return "T:" + typeName;
        }

        private static string MatchTypeSpecToTypeRef(string typeSpec, HashSet<string> typeRefs)
        {
            // do we have a type ref that matches?
            string bestMatch = "";
            foreach (var tr in typeRefs)
            {
                if (typeSpec.StartsWith(tr) && tr.Length > bestMatch.Length)
                    bestMatch = tr;
            }
            return bestMatch;
        }

        /// <summary>
        /// Add all assemblies that were referenced to the referenced assembly dictionary.  By default, 
        /// we add every referenced assembly and will remove the ones that are actually referenced when 
        /// all submitted assemblies are processed.
        /// </summary>
        /// <param name="metadataReader"></param>
        private void AddReferencedAssemblies(MetadataReader metadataReader)
        {
            var assemblyReferences = metadataReader.AssemblyReferences
                                        .Select(metadataReader.GetAssemblyReference)
                                        .Select(metadataReader.FormatAssemblyInfo);

            var assemblyName = metadataReader.FormatAssemblyInfo();

            foreach (var reference in assemblyReferences)
            {
                _unresolvedAssemblies.AddOrUpdate(
                    reference.ToString(),
                    new HashSet<string>(StringComparer.Ordinal) { assemblyName.ToString() },
                    (key, existing) =>
                    {
                        lock (existing)
                        {
                            existing.Add(assemblyName.ToString());
                        }

                        return existing;
                    });
            }
        }

        /// <summary>
        /// Attempt to get a MetadataReader.  Call this method instead of directly on PEReader so that
        /// exceptions thrown by it are caught and propagated as a known InvalidPEAssemblyException
        /// </summary>
        /// <param name="peReader"></param>
        /// <returns></returns>
        private MetadataReader GetMetadataReader(PEReader peReader)
        {
            try
            {
                return peReader.GetMetadataReader();
            }
            catch (Exception e)
            {
                throw new InvalidPEAssemblyException(e);
            }
        }

        private class InvalidPEAssemblyException : Exception
        {
            public InvalidPEAssemblyException(Exception inner)
                : base("Not a valid assembly", inner)
            { }
        }

        private class ConcurrentHashSet<T> : ConcurrentDictionary<T, byte>, ICollection<T>
        {
            public ConcurrentHashSet()
            { }

            public ConcurrentHashSet(IEqualityComparer<T> comparer)
                : base(comparer)
            { }

            public bool IsReadOnly { get; } = false;

            public void Add(T item) => TryAdd(item, 1);

            public bool Contains(T item) => ContainsKey(item);

            public void CopyTo(T[] array, int arrayIndex) => Keys.CopyTo(array, arrayIndex);

            public bool Remove(T item)
            {
                byte b;
                return TryRemove(item, out b);
            }

            IEnumerator IEnumerable.GetEnumerator() => Keys.GetEnumerator();

            IEnumerator<T> IEnumerable<T>.GetEnumerator() => Keys.GetEnumerator();
        }
    }
}
