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
        [Serializable]
        public struct ParameterDefinition
        {
            [Tooltip("Foxglove parameter name, e.g. /my_param")]
            public string Name;
            [Tooltip("JSON type hint: number, string, boolean, number[]")]
            public string Type;
            [TextArea(1, 2)]
            [Tooltip("Default value as JSON, e.g. 1.0, \"hello\", [1,0,0,1]")]
            public string DefaultValue;
            [Tooltip("Can Foxglove clients modify this parameter?")]
            public bool Writable;
        }

        [SerializeField] private List<ParameterDefinition> _parameters = new();

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
