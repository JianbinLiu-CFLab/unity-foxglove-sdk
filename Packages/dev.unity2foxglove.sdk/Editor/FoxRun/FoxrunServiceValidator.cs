// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Validates and groups FoxService methods for build-time generation.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    public static partial class FoxrunCodeGenerator
    {
        private static readonly PropertyInfo IsByRefLikeProperty =
            typeof(Type).GetProperty("IsByRefLike", BindingFlags.Instance | BindingFlags.Public);

        private static FoxServiceScanResult ScanFoxServiceMethods(bool ignoreReflectionTypeLoadExceptions)
        {
            var entries = new List<FoxServiceScanEntry>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (!type.IsClass || type.IsAbstract) continue;
                        if (!IsPartial(type)) continue;
                        if (!typeof(MonoBehaviour).IsAssignableFrom(type)) continue;

                        var methods = ScanServiceType(type);
                        if (methods.Count == 0) continue;

                        var ns = type.Namespace ?? "";
                        var key = (ns, type.Name);
                        var owner = string.IsNullOrEmpty(ns) ? type.Name : ns + "." + type.Name;

                        foreach (var method in methods)
                            entries.Add(new FoxServiceScanEntry(key, owner, method));
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    if (!ignoreReflectionTypeLoadExceptions)
                        throw;
                    WarnSkippedAssembly(asm, ex);
                }
            }

            return BuildFoxServiceScanResult(entries);
        }

        private static FoxServiceScanResult BuildFoxServiceScanResult(List<FoxServiceScanEntry> entries)
        {
            var duplicateNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in entries.GroupBy(entry => entry.Method.ServiceName, StringComparer.Ordinal))
            {
                if (group.Count() <= 1)
                    continue;

                duplicateNames.Add(group.Key);
                var owners = string.Join(", ", group.Select(entry => entry.Owner).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray());
                Debug.LogError("[FoxrunCodeGenerator] FOXSERVICE005: duplicate service name '" + group.Key + "' on " + owners + "; skipping duplicate generated service wrappers.");
            }

            var byClass = new Dictionary<(string Ns, string ClassName), List<FoxServiceSourceEmitter.ServiceMethod>>();
            foreach (var entry in entries
                         .Where(entry => !duplicateNames.Contains(entry.Method.ServiceName))
                         .OrderBy(entry => entry.Method.ServiceName, StringComparer.Ordinal))
            {
                if (!byClass.TryGetValue(entry.Key, out var list))
                    byClass[entry.Key] = list = new List<FoxServiceSourceEmitter.ServiceMethod>();
                list.Add(entry.Method);
            }

            return new FoxServiceScanResult(byClass);
        }

        private static List<FoxServiceSourceEmitter.ServiceMethod> ScanServiceType(Type type)
        {
            var result = new List<FoxServiceSourceEmitter.ServiceMethod>();
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
            var ns = type.Namespace ?? "";
            var className = type.Name;

            foreach (var method in type.GetMethods(flags))
            {
                if (method.IsSpecialName)
                    continue;

                var attrs = method.GetCustomAttributes<FoxServiceAttribute>();
                foreach (var attr in attrs)
                {
                    var target = (string.IsNullOrEmpty(ns) ? className : ns + "." + className) + "." + method.Name;
                    ValidateServiceMethod(target, method, attr, seenNames);

                    var parameters = method.GetParameters();
                    var hasRequest = parameters.Length == 1;
                    var requestType = hasRequest ? parameters[0].ParameterType : null;
                    var responseType = method.ReturnType;
                    var hasResponse = responseType != typeof(void);
                    var serviceType = string.IsNullOrWhiteSpace(attr.Type)
                        ? target
                        : attr.Type;
                    var requestSchemaName = string.IsNullOrWhiteSpace(attr.RequestSchemaName)
                        ? (hasRequest ? SchemaNameFromType(requestType) : serviceType + ".Request")
                        : attr.RequestSchemaName;
                    var responseSchemaName = string.IsNullOrWhiteSpace(attr.ResponseSchemaName)
                        ? (hasResponse ? SchemaNameFromType(responseType) : serviceType + ".Response")
                        : attr.ResponseSchemaName;
                    var requestSchema = FoxServiceSchemaEmitter.Emit(FoxServiceSchemaReflectionBuilder.Build(requestType, FoxServiceDtoRules.RequestSide));
                    var responseSchema = FoxServiceSchemaEmitter.Emit(FoxServiceSchemaReflectionBuilder.Build(hasResponse ? responseType : null, FoxServiceDtoRules.ResponseSide));

                    if (string.IsNullOrWhiteSpace(attr.Type)
                        || string.IsNullOrWhiteSpace(attr.RequestSchemaName)
                        || string.IsNullOrWhiteSpace(attr.ResponseSchemaName))
                        Debug.LogWarning("[FoxrunCodeGenerator] FOXSERVICE006: " + target + ": missing service type or schema metadata; generated defaults will be used.");

                    result.Add(new FoxServiceSourceEmitter.ServiceMethod(
                        method.Name,
                        attr.Name,
                        serviceType,
                        attr.Description,
                        requestSchemaName,
                        responseSchemaName,
                        requestSchema,
                        responseSchema,
                        hasRequest ? FoxRunEmissionTypeNameFormatter.FromReflectionType(requestType) : string.Empty,
                        hasResponse ? FoxRunEmissionTypeNameFormatter.FromReflectionType(responseType) : string.Empty,
                        hasRequest,
                        hasResponse));
                }
            }

            return result;
        }

        private static void ValidateServiceMethod(
            string target,
            MethodInfo method,
            FoxServiceAttribute attr,
            HashSet<string> seenNames)
        {
            if (attr == null || string.IsNullOrWhiteSpace(attr.Name) || !attr.Name.StartsWith("/", StringComparison.Ordinal))
                throw new InvalidOperationException("FOXSERVICE001: " + target + ": service name must be an absolute Foxglove service path.");

            if (!seenNames.Add(attr.Name))
                throw new InvalidOperationException("FOXSERVICE005: " + target + ": duplicate service name '" + attr.Name + "' within source.");

            var parameters = method.GetParameters();
            if (method.IsStatic
                || method.IsGenericMethod
                || method.GetCustomAttribute<AsyncStateMachineAttribute>() != null
                || parameters.Length > 1
                || parameters.Any(parameter => parameter.ParameterType.IsByRef || parameter.IsOut || parameter.IsDefined(typeof(ParamArrayAttribute), false))
                || IsUnsupportedServiceType(method.ReturnType)
                || parameters.Any(parameter => IsUnsupportedServiceType(parameter.ParameterType)))
                throw new InvalidOperationException("FOXSERVICE002: " + target + ": service methods must be non-static, synchronous, non-generic, and accept zero or one serializable DTO parameter.");

            if (parameters.Length == 1)
                ValidateServiceDtoType(target, attr.Name, parameters[0].ParameterType, FoxServiceDtoRules.RequestSide, "Request");

            if (method.ReturnType != typeof(void))
                ValidateServiceDtoType(target, attr.Name, method.ReturnType, FoxServiceDtoRules.ResponseSide, "Response");
        }

        private static bool IsUnsupportedServiceType(Type type)
        {
            if (type == null || type == typeof(void))
                return false;

            if (type.IsPointer || type.IsByRef || type.IsGenericParameter)
                return true;

            if (type.FullName == "System.Threading.Tasks.Task"
                || (type.FullName != null && type.FullName.StartsWith("System.Threading.Tasks.Task`", StringComparison.Ordinal)))
                return true;

            if (type.IsGenericType && type.GetGenericArguments().Any(argument => argument.IsGenericParameter))
                return true;

            return IsByRefLike(type);
        }

        private static string SchemaNameFromType(Type type)
            => type == null
                ? string.Empty
                : (type.FullName ?? type.Name).Replace('+', '.');

        private static bool IsByRefLike(Type type)
        {
            return IsByRefLikeProperty != null
                   && IsByRefLikeProperty.PropertyType == typeof(bool)
                   && type != null
                   && (bool)IsByRefLikeProperty.GetValue(type);
        }

        private static void ValidateServiceDtoType(string target, string serviceName, Type type, string side, string rootPath)
        {
            var dtoSide = side == FoxServiceDtoRules.RequestSide ? FoxServiceDtoSide.Request : FoxServiceDtoSide.Response;
            foreach (var diagnostic in FoxServiceDtoReflectionValidator.Validate(type, dtoSide, serviceName))
            {
                var message = diagnostic.Id + ": " + target + ": " + diagnostic.FormatTarget(serviceName);
                if (diagnostic.IsWarning)
                    Debug.LogWarning("[FoxrunCodeGenerator] " + message);
                else
                    throw new InvalidOperationException(message);
            }
        }

        private sealed class FoxServiceScanResult
        {
            public readonly Dictionary<(string Ns, string ClassName), List<FoxServiceSourceEmitter.ServiceMethod>> ByClass;

            public FoxServiceScanResult(Dictionary<(string Ns, string ClassName), List<FoxServiceSourceEmitter.ServiceMethod>> byClass)
            {
                ByClass = byClass;
            }
        }

        private sealed class FoxServiceScanEntry
        {
            public readonly (string Ns, string ClassName) Key;
            public readonly string Owner;
            public readonly FoxServiceSourceEmitter.ServiceMethod Method;

            public FoxServiceScanEntry((string Ns, string ClassName) key, string owner, FoxServiceSourceEmitter.ServiceMethod method)
            {
                Key = key;
                Owner = owner ?? string.Empty;
                Method = method;
            }
        }
    }
}
