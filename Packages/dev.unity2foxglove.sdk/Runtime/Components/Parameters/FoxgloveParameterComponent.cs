// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Parameters
// Purpose: Declares Foxglove parameters from the Unity Inspector.
// Automatically registers with FoxgloveManager on enable.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
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

        /// <summary>
        /// Registers all defined parameters with the FoxgloveManager on this GameObject.
        /// Skips parameters with empty names and logs warnings on parse errors.
        /// </summary>
        private void OnEnable()
        {
            var manager = GetComponent<FoxgloveManager>();
            if (manager == null)
            {
                Debug.LogWarning("[Foxglove] FoxgloveParameterComponent requires a FoxgloveManager on the same GameObject.");
                return;
            }

            foreach (var p in _parameters)
            {
                if (string.IsNullOrEmpty(p.Name)) continue;
                try
                {
                    var value = string.IsNullOrEmpty(p.DefaultValue)
                        ? JToken.FromObject(0)
                        : JToken.Parse(p.DefaultValue);
                    manager.RegisterParameter(p.Name, value, p.Type, p.Writable);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Foxglove] Failed to register parameter '{p.Name}': {ex.Message}");
                }
            }
        }
    }
}
