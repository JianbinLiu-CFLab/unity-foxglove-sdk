// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Structured reflection-side FoxService DTO validation for Player fallback generation.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxServiceDtoReflectionValidator
    {
        private static readonly PropertyInfo IsByRefLikeProperty =
            typeof(Type).GetProperty("IsByRefLike", BindingFlags.Instance | BindingFlags.Public);

        public static IReadOnlyList<FoxServiceDtoDiagnostic> Validate(Type rootType, FoxServiceDtoSide side, string serviceName)
        {
            _ = serviceName;
            var diagnostics = new List<FoxServiceDtoDiagnostic>();
            var stack = new HashSet<string>(StringComparer.Ordinal);
            var validatedTypes = new HashSet<string>(StringComparer.Ordinal);
            var rootPath = side == FoxServiceDtoSide.Request ? "Request" : "Response";
            ValidateType(
                rootType,
                side.ToRuleSide(),
                rootPath,
                rootType,
                diagnostics,
                stack,
                validatedTypes,
                0);
            return diagnostics;
        }

        private static void ValidateType(
            Type type,
            string side,
            string path,
            Type rootType,
            List<FoxServiceDtoDiagnostic> diagnostics,
            HashSet<string> stack,
            HashSet<string> validatedTypes,
            int depth)
        {
            if (type == null || type == typeof(void))
                return;

            type = Nullable.GetUnderlyingType(type) ?? type;
            var typeName = DiagnosticTypeName(type);
            var rootName = DiagnosticTypeName(rootType);

            if (depth > FoxServiceDtoRules.MaxDepth)
            {
                AddDtoDiagnostic(FoxServiceDtoRules.DepthDiagnosticId, true, side, rootName, path, typeName, "DTO graph exceeds the supported traversal depth.", diagnostics);
                return;
            }

            if (type.IsPointer || type.IsByRef || type.IsGenericParameter)
            {
                AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "Pointer and open generic DTO members cannot be serialized safely.", diagnostics);
                return;
            }

            if (type.IsArray)
            {
                if (type.GetArrayRank() != 1)
                {
                    AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "Only single-dimensional arrays are supported.", diagnostics);
                    return;
                }

                ValidateType(type.GetElementType(), side, path, rootType, diagnostics, stack, validatedTypes, depth + 1);
                return;
            }

            if (IsScalarDtoType(type) || type.IsEnum)
                return;

            var fullName = FullTypeName(type);
            var stackKey = fullName;
            if (validatedTypes.Contains(stackKey))
                return;

            if (FoxServiceDtoTypeNames.IsTaskLike(fullName)
                || FoxServiceDtoTypeNames.IsUnsafeRuntimeHandle(fullName)
                || FoxServiceDtoTypeNames.IsFunctionPointerLike(fullName)
                || typeof(Delegate).IsAssignableFrom(type)
                || IsUnityObjectLike(type)
                || type == typeof(object)
                || IsByRefLike(type))
            {
                AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "DTO member type is not JSON DTO serializable.", diagnostics);
                return;
            }

            if (TryGetDictionaryValueType(type, out var keyType, out var valueType))
            {
                if (keyType != typeof(string))
                {
                    AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "Dictionary DTO members must use string keys.", diagnostics);
                    return;
                }

                ValidateType(valueType, side, path, rootType, diagnostics, stack, validatedTypes, depth + 1);
                return;
            }

            if (TryGetListElementType(type, side, out var elementType))
            {
                ValidateType(elementType, side, path, rootType, diagnostics, stack, validatedTypes, depth + 1);
                return;
            }

            if (type.IsInterface)
            {
                AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "Interface DTO members are unsupported unless they are a known collection contract.", diagnostics);
                return;
            }

            if (!stack.Add(stackKey))
            {
                AddDtoDiagnostic(FoxServiceDtoRules.CycleDiagnosticId, false, side, rootName, path, typeName, "DTO graph contains a recursive reference.", diagnostics);
                return;
            }

            var diagnosticCountBeforeMembers = diagnostics.Count;
            foreach (var member in FoxServiceDtoReflectionMembers.SerializableMembers(type))
            {
                if (member is FieldInfo field)
                {
                    if (field.IsStatic || field.IsLiteral)
                        continue;
                    if (FoxServiceDtoReflectionMembers.IsIgnored(field))
                    {
                        AddDtoWarning(side, rootName, path + "." + field.Name, DiagnosticTypeName(field.FieldType), "Member is ignored by serialization attributes.", diagnostics);
                        continue;
                    }
                    if (field.IsInitOnly)
                    {
                        AddDtoWarning(side, rootName, path + "." + field.Name, DiagnosticTypeName(field.FieldType), "Readonly fields may serialize but may not round-trip from request JSON.", diagnostics);
                        continue;
                    }
                    ValidateType(field.FieldType, side, path + "." + field.Name, rootType, diagnostics, stack, validatedTypes, depth + 1);
                }
                else if (member is PropertyInfo property)
                {
                    if (property.GetIndexParameters().Length != 0 || property.GetMethod == null)
                        continue;
                    if (FoxServiceDtoReflectionMembers.IsIgnored(property))
                    {
                        AddDtoWarning(side, rootName, path + "." + property.Name, DiagnosticTypeName(property.PropertyType), "Member is ignored by serialization attributes.", diagnostics);
                        continue;
                    }
                    if (property.SetMethod == null)
                    {
                        if (TryGetListElementType(property.PropertyType, side, out var getOnlyElementType)
                            && IsMutableCollectionContract(property.PropertyType))
                        {
                            ValidateType(getOnlyElementType, side, path + "." + property.Name, rootType, diagnostics, stack, validatedTypes, depth + 1);
                            continue;
                        }

                        AddDtoWarning(side, rootName, path + "." + property.Name, DiagnosticTypeName(property.PropertyType), "Get-only properties are not populated during request deserialization.", diagnostics);
                        continue;
                    }
                    ValidateType(property.PropertyType, side, path + "." + property.Name, rootType, diagnostics, stack, validatedTypes, depth + 1);
                }
            }

            stack.Remove(stackKey);
            if (diagnostics.Count == diagnosticCountBeforeMembers)
                validatedTypes.Add(stackKey);
        }

        private static bool IsScalarDtoType(Type type)
            => FoxServiceDtoTypeNames.IsScalar(FullTypeName(type));

        private static bool TryGetListElementType(Type type, string side, out Type elementType)
        {
            elementType = null;
            if (!type.IsGenericType)
                return false;

            var contract = GenericContractName(type.GetGenericTypeDefinition());
            if (!FoxServiceDtoTypeNames.IsListContract(contract, side))
                return false;

            elementType = type.GetGenericArguments()[0];
            return true;
        }

        private static bool TryGetDictionaryValueType(Type type, out Type keyType, out Type valueType)
        {
            keyType = null;
            valueType = null;
            if (!type.IsGenericType)
                return false;

            var contract = GenericDictionaryContractName(type.GetGenericTypeDefinition());
            if (!FoxServiceDtoTypeNames.IsDictionaryContract(contract))
                return false;

            var arguments = type.GetGenericArguments();
            keyType = arguments[0];
            valueType = arguments[1];
            return true;
        }

        private static bool IsMutableCollectionContract(Type type)
            => type.IsGenericType
               && FoxServiceDtoTypeNames.IsMutableCollectionContract(GenericContractName(type.GetGenericTypeDefinition()));

        private static string GenericContractName(Type definition)
        {
            if (definition == typeof(List<>)) return "System.Collections.Generic.List<T>";
            if (definition == typeof(IList<>)) return "System.Collections.Generic.IList<T>";
            if (definition == typeof(IReadOnlyList<>)) return "System.Collections.Generic.IReadOnlyList<T>";
            if (definition == typeof(HashSet<>)) return "System.Collections.Generic.HashSet<T>";
            if (definition == typeof(ICollection<>)) return "System.Collections.Generic.ICollection<T>";
            if (definition == typeof(IReadOnlyCollection<>)) return "System.Collections.Generic.IReadOnlyCollection<T>";
            if (definition == typeof(Queue<>)) return "System.Collections.Generic.Queue<T>";
            if (definition == typeof(Stack<>)) return "System.Collections.Generic.Stack<T>";
            if (definition == typeof(Collection<>)) return "System.Collections.ObjectModel.Collection<T>";
            return FullTypeName(definition);
        }

        private static string GenericDictionaryContractName(Type definition)
        {
            if (definition == typeof(Dictionary<,>)) return "System.Collections.Generic.Dictionary<TKey, TValue>";
            if (definition == typeof(IDictionary<,>)) return "System.Collections.Generic.IDictionary<TKey, TValue>";
            if (definition == typeof(IReadOnlyDictionary<,>)) return "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>";
            if (definition == typeof(SortedDictionary<,>)) return "System.Collections.Generic.SortedDictionary<TKey, TValue>";
            return FullTypeName(definition);
        }

        private static bool IsByRefLike(Type type)
            => IsByRefLikeProperty != null
               && IsByRefLikeProperty.PropertyType == typeof(bool)
               && type != null
               && (bool)IsByRefLikeProperty.GetValue(type);

        private static bool IsUnityObjectLike(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (FullTypeName(current) == "UnityEngine.Object")
                    return true;
            }

            return false;
        }

        private static string FullTypeName(Type type)
            => FoxServiceDtoTypeNames.Normalize(type == null ? string.Empty : (type.FullName ?? type.Name));

        private static string DiagnosticTypeName(Type type)
        {
            if (type == null)
                return string.Empty;
            if (type == typeof(object))
                return "object";
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
                return type.Name;
            return FullTypeName(type);
        }

        private static void AddUnsupportedDtoDiagnostic(
            string side,
            string rootType,
            string path,
            string offendingType,
            string reason,
            List<FoxServiceDtoDiagnostic> diagnostics)
            => AddDtoDiagnostic(FoxServiceDtoRules.UnsupportedDiagnosticId(side), false, side, rootType, path, offendingType, reason, diagnostics);

        private static void AddDtoWarning(
            string side,
            string rootType,
            string path,
            string offendingType,
            string reason,
            List<FoxServiceDtoDiagnostic> diagnostics)
            => AddDtoDiagnostic(FoxServiceDtoRules.WarningDiagnosticId, true, side, rootType, path, offendingType, reason, diagnostics);

        private static void AddDtoDiagnostic(
            string id,
            bool isWarning,
            string side,
            string rootType,
            string path,
            string offendingType,
            string reason,
            List<FoxServiceDtoDiagnostic> diagnostics)
            => diagnostics.Add(new FoxServiceDtoDiagnostic(id, isWarning, side, rootType, path, offendingType, reason));
    }
}
