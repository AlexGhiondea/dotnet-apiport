﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Fx.Portability.ObjectModel;
using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Security.Cryptography;

namespace Microsoft.Fx.Portability.Analyzer
{
    internal class DependencyFinderEngineHelper
    {
        private readonly IDependencyFilter _assemblyFilter;
        private readonly MetadataReader _reader;
        private readonly SystemObjectFinder _objectFinder;
        private readonly AssemblyReferenceInformation _currentAssemblyInfo;
        private readonly string _currentAssemblyName;

        public DependencyFinderEngineHelper(IDependencyFilter assemblyFilter, MetadataReader metadataReader, IAssemblyFile file, SystemObjectFinder objectFinder)
        {
            _assemblyFilter = assemblyFilter;
            _reader = metadataReader;
            _objectFinder = objectFinder;

            MemberDependency = new List<MemberDependency>();
            CallingAssembly = new AssemblyInfo
            {
                Location = file.Name,
                AssemblyIdentity = metadataReader.FormatAssemblyInfo().ToString(),
                FileVersion = file.Version ?? string.Empty,
                TargetFrameworkMoniker = metadataReader.GetTargetFrameworkMoniker() ?? string.Empty,
                AssemblyReferences = ComputeAssemblyReferences(metadataReader)
            };

            // Get assembly info
            var assemblyDefinition = _reader.GetAssemblyDefinition();

            _currentAssemblyInfo = _reader.FormatAssemblyInfo(assemblyDefinition);
            _currentAssemblyName = _reader.GetString(assemblyDefinition.Name);
        }

        private IList<AssemblyReferenceInformation> ComputeAssemblyReferences(MetadataReader metadataReader)
        {
            List<AssemblyReferenceInformation> refs = new List<AssemblyReferenceInformation>();
            foreach (var handle in _reader.AssemblyReferences)
            {
                try
                {
                    var entry = _reader.GetAssemblyReference(handle);

                    string name = metadataReader.GetString(entry.Name);
                    string culture = entry.Culture.IsNil ? "neutral" : metadataReader.GetString(entry.Culture);
                    Version version = entry.Version;
                    string pkt = FormatPublicKeyToken(metadataReader, entry.PublicKeyOrToken);

                    refs.Add(new AssemblyReferenceInformation(name, version, culture, pkt));
                }
                catch (BadImageFormatException)
                {
                }
            }
            return refs;
        }

        private static string FormatPublicKeyToken(MetadataReader metadataReader, BlobHandle handle)
        {
            byte[] bytes = metadataReader.GetBlobBytes(handle);

            if (bytes == null || bytes.Length <= 0)
            {
                return "null";
            }

            if (bytes.Length > 8)  // Strong named assembly
            {
                // Get the public key token, which is the last 8 bytes of the SHA-1 hash of the public key 
                using (var sha1 = SHA1.Create())
                {
                    var token = sha1.ComputeHash(bytes);

                    bytes = new byte[8];
                    int count = 0;
                    for (int i = token.Length - 1; i >= token.Length - 8; i--)
                    {
                        bytes[count] = token[i];
                        count++;
                    }
                }
            }

            // Convert bytes to string, but we don't want the '-' characters and need it to be lower case
            return BitConverter.ToString(bytes)
                .Replace("-", "")
                .ToLowerInvariant();
        }

        public AssemblyInfo CallingAssembly { get; }

        public IList<MemberDependency> MemberDependency { get; }

        public void ComputeData()
        {
            // Primitives need to have their assembly set, so we search for a
            // reference to System.Object that is considered a possible
            // framework assembly and use that for any primitives that don't
            // have an assembly
            var systemObjectAssembly = _objectFinder.GetSystemRuntimeAssemblyInformation(_reader);

            var provider = new MemberMetadataInfoTypeProvider(_reader);

            // Get type references
            foreach (var handle in _reader.TypeReferences)
            {
                try
                {
                    var entry = _reader.GetTypeReference(handle);
                    var typeInfo = provider.GetFullName(entry);
                    var assembly = GetAssembly(typeInfo);
                    var typeReferenceMemberDependency = CreateMemberDependency(typeInfo, assembly);

                    if (typeReferenceMemberDependency != null)
                    {
                        MemberDependency.Add(typeReferenceMemberDependency);
                    }
                }
                catch (BadImageFormatException)
                {
                    // Some obfuscators will inject dead types that break decompilers
                    // (for example, types that serve as each others' scopes).
                    //
                    // For portability/compatibility analysis purposes, though,
                    // we can skip such malformed references and just analyze those
                    // that we can successfully decode.
                }
            }

            // Get member references
            foreach (var handle in _reader.MemberReferences)
            {
                try
                {
                    var entry = _reader.GetMemberReference(handle);

                    var memberReferenceMemberDependency = GetMemberReferenceMemberDependency(entry, systemObjectAssembly);
                    if (memberReferenceMemberDependency != null)
                    {
                        MemberDependency.Add(memberReferenceMemberDependency);
                    }
                }
                catch (BadImageFormatException)
                {
                    // Some obfuscators will inject dead types that break decompilers
                    // (for example, types that serve as each others' scopes).
                    //
                    // For portability/compatibility analysis purposes, though,
                    // we can skip such malformed references and just analyze those
                    // that we can successfully decode.
                }
            }
        }

        private AssemblyReferenceInformation GetAssembly(MemberMetadataInfo type)
        {
            return type.DefinedInAssembly.HasValue ? _reader.FormatAssemblyInfo(type.DefinedInAssembly.Value) : _currentAssemblyInfo;
        }

        private MemberDependency CreateMemberDependency(MemberMetadataInfo type)
        {
            return CreateMemberDependency(type, GetAssembly(type));
        }

        private MemberDependency CreateMemberDependency(MemberMetadataInfo type, AssemblyReferenceInformation definedInAssembly)
        {
            // Apply heuristic to determine if API is most likely defined in a framework assembly
            if (!_assemblyFilter.IsFrameworkAssembly(definedInAssembly))
            {
                return null;
            }

            return new MemberDependency
            {
                CallingAssembly = CallingAssembly,
                MemberDocId = FormattableString.Invariant($"T:{type}"),
                DefinedInAssemblyIdentity = definedInAssembly
            };
        }

        private MemberDependency GetMemberReferenceMemberDependency(MemberReference memberReference, AssemblyReferenceInformation systemObjectAssembly)
        {
            var provider = new MemberMetadataInfoTypeProvider(_reader);
            var memberRefInfo = provider.GetMemberRefInfo(memberReference);

            AssemblyReferenceInformation definedInAssemblyIdentity = null;
            if (memberRefInfo.ParentType.DefinedInAssembly.HasValue)
            {
                definedInAssemblyIdentity = _reader.FormatAssemblyInfo(memberRefInfo.ParentType.DefinedInAssembly.Value);
            }
            else if (memberRefInfo.ParentType.IsPrimitiveType)
            {
                definedInAssemblyIdentity = systemObjectAssembly;
            }
            else
            {
                definedInAssemblyIdentity = _currentAssemblyInfo;
            }

            // Apply heuristic to determine if API is most likely defined in a framework assembly
            if (!_assemblyFilter.IsFrameworkAssembly(definedInAssemblyIdentity))
            {
                return null;
            }

            // Add the parent type to the types list (only needed when we want to report memberrefs defined in the current assembly)
            if (memberRefInfo.ParentType.IsTypeDef || (memberRefInfo.ParentType.IsPrimitiveType && _currentAssemblyName.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)))
            {
                var memberDependency = CreateMemberDependency(memberRefInfo.ParentType);

                if (memberDependency != null)
                {
                    MemberDependency.Add(memberDependency);
                }
            }

            return new MemberDependency
            {
                CallingAssembly = CallingAssembly,
                MemberDocId = FormattableString.Invariant($"{GetPrefix(memberReference)}:{memberRefInfo}"),
                TypeDocId = FormattableString.Invariant($"T:{memberRefInfo.ParentType}"),
                IsPrimitive = memberRefInfo.ParentType.IsPrimitiveType,
                DefinedInAssemblyIdentity = definedInAssemblyIdentity
            };
        }

        private static string GetPrefix(MemberReference memberReference)
        {
            switch (memberReference.GetKind())
            {
                case MemberReferenceKind.Field:
                    return "F";
                case MemberReferenceKind.Method:
                    return "M";
                default:
                    return memberReference.GetKind().ToString();
            }
        }
    }
}
