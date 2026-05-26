// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Parameters
// Purpose: Declares Foxglove parameters from the Unity Inspector.
// Automatically registers with FoxgloveManager on enable.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Declares foxglove parameters from the Unity Inspector.
    /// Automatically registers with FoxgloveManager on enable.
    /// </summary>
    public class FoxgloveParameterComponent : MonoBehaviour
    {
        /// <summary>
        /// A single Foxglove parameter definition configured in the Inspector.
        /// </summary>
        [Serializable]
        public struct ParameterDefinition
        {
            /// <summary>Foxglove parameter name, e.g. <c>/my_param</c>.</summary>
            [Tooltip("Foxglove parameter name, e.g. /my_param")]
            public string Name;
            /// <summary>JSON type hint: <c>number</c>, <c>string</c>, <c>boolean</c>, <c>number[]</c>.</summary>
            [Tooltip("JSON type hint: number, string, boolean, number[]")]
            public string Type;
            /// <summary>Default value as JSON, e.g. <c>1.0</c>, <c>"hello"</c>, <c>[1,0,0,1]</c>.</summary>
            [TextArea(1, 2)]
            [Tooltip("Default value as JSON, e.g. 1.0, \"hello\", [1,0,0,1]")]
            public string DefaultValue;
            /// <summary>Whether Foxglove clients can modify this parameter.</summary>
            [Tooltip("Can Foxglove clients modify this parameter?")]
            public bool Writable;
        }

        // ── Serialized fields ──
        /// <summary>List of parameter definitions to register on enable.</summary>
        [SerializeField] private List<ParameterDefinition> _parameters = new();
        private readonly List<string> _registeredNames = new();
        private FoxgloveManager _registeredManager;

        /// <summary>
        /// Registers all defined parameters with the FoxgloveManager on this GameObject.
        /// Skips parameters with empty names and logs warnings on parse errors.
        /// </summary>
        private void OnEnable()
        {
            UnregisterRegisteredParameters();

            var manager = GetComponent<FoxgloveManager>();
            if (manager == null)
            {
                Debug.LogWarning("[Foxglove] FoxgloveParameterComponent requires a FoxgloveManager on the same GameObject.");
                return;
            }

            _registeredManager = manager;
            foreach (var p in _parameters)
            {
                if (string.IsNullOrEmpty(p.Name)) continue;
                try
                {
                    var value = string.IsNullOrEmpty(p.DefaultValue)
                        ? FoxgloveParameterStore.DefaultValueForType(p.Type)
                        : JToken.Parse(p.DefaultValue);
                    if (!FoxgloveParameterStore.TryNormalizeValueForType(p.Type, value, out var normalizedValue))
                    {
                        Debug.LogWarning($"[Foxglove] Failed to register parameter '{p.Name}': default value does not match type '{p.Type}'.");
                        continue;
                    }

                    manager.RegisterParameter(
                        p.Name,
                        normalizedValue,
                        FoxgloveParameterStore.NormalizeParameterType(p.Type),
                        p.Writable);
                    _registeredNames.Add(p.Name);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Foxglove] Failed to register parameter '{p.Name}': {ex.Message}");
                }
            }
        }

        private void OnDisable()
        {
            UnregisterRegisteredParameters();
        }

        private void OnDestroy()
        {
            UnregisterRegisteredParameters();
        }

        private void UnregisterRegisteredParameters()
        {
            if (_registeredManager != null)
            {
                foreach (var name in _registeredNames)
                    _registeredManager.UnregisterParameter(name);
            }

            _registeredNames.Clear();
            _registeredManager = null;
        }
    }
}
