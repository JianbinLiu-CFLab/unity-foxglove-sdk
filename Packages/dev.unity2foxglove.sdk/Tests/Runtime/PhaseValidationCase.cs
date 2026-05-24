// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Describes one registry-driven validation entry for the runtime test runner.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    internal sealed class PhaseValidationCase
    {
        public PhaseValidationCase(
            string flag,
            string name,
            ValidationCategory category,
            Action run,
            bool includeInDefault,
            params string[] aliases)
        {
            Flag = flag;
            Name = name;
            Category = category;
            Run = run;
            IncludeInDefault = includeInDefault;
            Aliases = aliases ?? Array.Empty<string>();
        }

        public string Flag { get; }
        public string Name { get; }
        public ValidationCategory Category { get; }
        public Action Run { get; }
        public bool IncludeInDefault { get; }
        public IReadOnlyList<string> Aliases { get; }

        public IEnumerable<string> AllFlags()
        {
            if (!string.IsNullOrEmpty(Flag))
                yield return Flag;

            foreach (var alias in Aliases)
                yield return alias;
        }

        public bool Matches(IReadOnlyCollection<string> args)
        {
            return AllFlags().Any(args.Contains);
        }
    }
}
